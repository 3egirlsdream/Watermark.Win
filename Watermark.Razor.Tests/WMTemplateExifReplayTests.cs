using SkiaSharp;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMTemplateExifReplayTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"watermark-template-exif-{Guid.NewGuid():N}");

    [Fact]
    public async Task Preview_ReattachesSourceExif_WithoutAdditionalPipelinePasses()
    {
        var sourcePath = CreateSource("source.png");
        var canvas = new WMCanvas { ID = "template-exif", Name = "EXIF template" };
        var session = CreateSession(
            sourcePath,
            canvas,
            new Dictionary<string, string>
            {
                ["Make"] = "Fujifilm",
                ["Model"] = "X-T5",
                ["ISOSpeedRatings"] = "ISO 320"
            });
        var renderer = new CapturingTemplateRenderer();
        var metrics = new WMWorkspacePerformanceCounters();
        using var scheduler = new WMProcessingScheduler();
        var service = new WMWorkspacePreviewService(
            renderer,
            new WMColorGradeOperationProcessor(scheduler),
            scheduler,
            new TestProfiles(),
            metrics);
        var plan = await new WMRenderPlanCompiler().CompileAsync(
            session,
            session.CurrentMediaId!,
            WMRenderTarget.SettledPreview(),
            CancellationToken.None);

        var first = await service.RenderAsync(plan, 1, CancellationToken.None);
        var firstMetrics = metrics.Snapshot();

        Assert.True(File.Exists(first.FilePath));
        Assert.Equal(1, renderer.RenderCalls);
        var runtimeExif = Assert.Single(renderer.CanvasExif).Value;
        Assert.Equal("Fujifilm", runtimeExif["Make"]);
        Assert.Equal("X-T5", runtimeExif["Model"]);
        Assert.Equal("ISO 320", runtimeExif["ISOSpeedRatings"]);
        Assert.Equal(1, firstMetrics.Calls[WMWorkspaceMetricStage.Decode]);
        Assert.Equal(1, firstMetrics.Calls[WMWorkspaceMetricStage.Replay]);
        Assert.Equal(1, firstMetrics.Calls[WMWorkspaceMetricStage.Encode]);

        var second = await service.RenderAsync(plan, 2, CancellationToken.None);
        var secondMetrics = metrics.Snapshot();

        Assert.True(second.CacheHit);
        Assert.Equal(first.FilePath, second.FilePath);
        Assert.Equal(1, renderer.RenderCalls);
        Assert.Equal(firstMetrics.Calls, secondMetrics.Calls);
    }

    [Fact]
    public async Task TemplateGraphFingerprint_IncludesExifForPixelIdenticalArtifacts()
    {
        var sourcePath = CreateSource("fingerprint-source.png");
        var canvas = new WMCanvas { ID = "template-fingerprint", Name = "Fingerprint template" };
        var firstSession = CreateSession(
            sourcePath,
            canvas,
            new Dictionary<string, string> { ["Model"] = "Camera A" });
        var secondSession = CreateSession(
            sourcePath,
            canvas,
            new Dictionary<string, string> { ["Model"] = "Camera B" });
        var compiler = new WMRenderPlanCompiler();

        var first = await compiler.CompileAsync(
            firstSession, firstSession.CurrentMediaId!, WMRenderTarget.SettledPreview(), CancellationToken.None);
        var second = await compiler.CompileAsync(
            secondSession, secondSession.CurrentMediaId!, WMRenderTarget.SettledPreview(), CancellationToken.None);

        Assert.Equal(
            first.Steps.Select(step => step.Operation.ParametersJson),
            second.Steps.Select(step => step.Operation.ParametersJson));
        Assert.NotEqual(first.GraphFingerprint, second.GraphFingerprint);
    }

    [Fact]
    public async Task FastJpegExport_ReattachesSourceExifThroughTheSharedReplayEntry()
    {
        var sourcePath = CreateSource("export-source.png");
        var canvas = new WMCanvas { ID = "template-export-exif", Name = "Export EXIF template" };
        var session = CreateSession(
            sourcePath,
            canvas,
            new Dictionary<string, string>
            {
                ["Make"] = "Sony",
                ["Model"] = "ILCE-7M4"
            });
        var compiled = await new WMRenderPlanCompiler().CompileAsync(
            session,
            session.CurrentMediaId!,
            new WMRenderTarget(WMRenderPurpose.Export, null, WMExportFormat.Jpeg8, 92, true),
            CancellationToken.None);
        var renderer = new CapturingTemplateRenderer();
        using var scheduler = new WMProcessingScheduler();
        var service = new WMFastJpegExportService(
            renderer,
            new WMColorGradeOperationProcessor(scheduler),
            scheduler);
        var outputPath = Path.Combine(root, "export.jpg");

        await service.RenderAsync(new WMFullResolutionRenderRequest(
            compiled.ToRenderPlan(),
            outputPath,
            sourcePath,
            "default",
            92,
            Path.Combine(root, "export-working"),
            new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 }));

        Assert.True(File.Exists(outputPath));
        Assert.Equal(1, renderer.RenderCalls);
        var runtimeExif = Assert.Single(renderer.CanvasExif).Value;
        Assert.Equal("Sony", runtimeExif["Make"]);
        Assert.Equal("ILCE-7M4", runtimeExif["Model"]);
    }

    [Fact]
    public void HighPrecisionTemplateClone_PreservesRuntimeExif()
    {
        const int width = 320;
        const int height = 200;
        var inputPath = Path.Combine(root, "high-exif-input.wm16");
        var firstOutput = Path.Combine(root, "high-exif-a.wm16");
        var secondOutput = Path.Combine(root, "high-exif-b.wm16");
        Directory.CreateDirectory(root);
        var samples = new ushort[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            samples[pixel * 4] = 38_000;
            samples[pixel * 4 + 1] = 38_000;
            samples[pixel * 4 + 2] = 38_000;
            samples[pixel * 4 + 3] = ushort.MaxValue;
        }
        using (var writer = new WM16FileWriter(inputPath, width, height, 4, height))
        {
            writer.WriteTile(new WMLinearTile(0, height, width, 4, samples));
            writer.Complete();
        }
        var canvas = CreateExifTextCanvas();
        var renderer = new WMHighPrecisionTemplateRenderer(new WatermarkHelper());
        canvas.Exif[canvas.ID] = new Dictionary<string, string> { ["Model"] = "Camera Alpha" };

        var first = renderer.Render(inputPath, firstOutput, canvas);
        canvas.Exif[canvas.ID]["Model"] = "Camera Beta";
        var second = renderer.Render(inputPath, secondOutput, canvas);

        Assert.NotEqual(first.ContentHash, second.ContentHash);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }

    private string CreateSource(string fileName)
    {
        var previewDirectory = Path.Combine(root, "session", "previews");
        Directory.CreateDirectory(previewDirectory);
        var path = Path.Combine(previewDirectory, fileName);
        using var bitmap = new SKBitmap(32, 24, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(new SKColor(80, 120, 180));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static WMWorkspaceSession CreateSession(
        string sourcePath,
        WMCanvas canvas,
        IReadOnlyDictionary<string, string> exif)
    {
        var artifact = new WMImageArtifact
        {
            Id = "source-artifact",
            FilePath = sourcePath,
            PreviewPath = sourcePath,
            ContentHash = "pixel-identical-content",
            CanvasSnapshotJson = Global.CanvasSerialize(canvas),
            Width = 32,
            Height = 24,
            Exif = new Dictionary<string, string>(exif)
        };
        var media = new WMWorkspaceMedia
        {
            Id = "media",
            DisplayName = Path.GetFileName(sourcePath),
            OriginalReference = sourcePath,
            Artifact = artifact
        };
        return new WMWorkspaceSession
        {
            Id = "session",
            Media = [media],
            MediaCatalog = [media],
            ActiveMediaIds = [media.Id],
            CurrentMediaId = media.Id,
            TemplateId = canvas.ID
        };
    }

    private static WMCanvas CreateExifTextCanvas()
    {
        var canvas = new WMCanvas { ID = "high-exif-template", Name = "High EXIF template" };
        var container = new WMContainer
        {
            WidthPercent = 100,
            HeightPercent = 30,
            ContainerAlignment = Watermark.Shared.Enums.ContainerAlignment.Bottom,
            BackgroundColor = "#FFFFFFFF"
        };
        var text = new WMText
        {
            FontSize = 60,
            FontColor = "#000000FF"
        };
        text.Exifs.Add(new WMExifConfigInfo { Key = "Model" });
        container.Controls.Add(text);
        canvas.Children.Add(container);
        return canvas;
    }

    private sealed class CapturingTemplateRenderer : IWMTemplateRenderer
    {
        public int RenderCalls { get; private set; }
        public IReadOnlyDictionary<string, Dictionary<string, string>> CanvasExif { get; private set; }
            = new Dictionary<string, Dictionary<string, string>>();

        public Task<byte[]> RenderAsync(
            WMTemplateRenderRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public SKBitmap RenderBitmap(
            WMCanvas canvas,
            SKBitmap source,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderCalls++;
            CanvasExif = canvas.Exif.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value));
            return source.Copy();
        }
    }

    private sealed class TestProfiles : IWMExecutionProfileProvider
    {
        public WMOperationExecutionOptions GetInteractiveProfile() => new()
        {
            MaxConcurrentImages = 1,
            MaxPixelWorkers = 1,
            PreviewMaxEdge = 1600
        };

        public WMImagingCapabilities GetImagingCapabilities() => WMImagingCapabilities.MobileDisabled;
    }
}
