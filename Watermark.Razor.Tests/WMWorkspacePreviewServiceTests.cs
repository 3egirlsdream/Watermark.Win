using SkiaSharp;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWorkspacePreviewServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"watermark-fused-preview-{Guid.NewGuid():N}");
    private string? templateDirectory;

    [Fact]
    public async Task TemplateAndColorGrade_UseOneDecodeReplayAndEncode_ThenReuseDiskArtifact()
    {
        var sessionDirectory = Path.Combine(root, "session");
        var previewDirectory = Path.Combine(sessionDirectory, "previews");
        Directory.CreateDirectory(previewDirectory);
        var sourcePath = Path.Combine(previewDirectory, "source.png");
        WriteSource(sourcePath);

        var canvas = new WMCanvas { Name = "fused-preview" };
        templateDirectory = Path.Combine(Global.AppPath.TemplatesFolder, canvas.ID);
        Directory.CreateDirectory(templateDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(templateDirectory, "config.json"),
            Global.CanvasSerialize(canvas));

        var recipe = new WMColorRecipe { Name = "test" };
        recipe.Normalize();
        recipe.Grade.Contrast = 12;
        recipe.UserAdjustments!.Contrast = 12;
        var artifact = new WMImageArtifact
        {
            Id = "source-artifact",
            FilePath = sourcePath,
            PreviewPath = sourcePath,
            ContentHash = "source-content",
            CanvasSnapshotJson = Global.CanvasSerialize(canvas),
            Width = 32,
            Height = 24
        };
        var media = new WMWorkspaceMedia
        {
            Id = "media",
            DisplayName = "source.png",
            OriginalReference = sourcePath,
            Artifact = artifact
        };
        var session = new WMWorkspaceSession
        {
            Id = "session",
            Media = [media],
            CurrentMediaId = media.Id,
            TemplateId = canvas.ID,
            ColorRecipe = recipe
        };
        var metrics = new WMWorkspacePerformanceCounters();
        var renderer = new CountingTemplateRenderer();
        var scheduler = new WMProcessingScheduler();
        var service = new WMWorkspacePreviewService(
            renderer,
            new WMColorGradeOperationProcessor(scheduler),
            scheduler,
            new TestProfiles(),
            metrics);
        var plan = await new WMRenderPlanCompiler().CompileAsync(
            session,
            media.Id,
            WMRenderTarget.SettledPreview(),
            CancellationToken.None);

        var first = await service.RenderAsync(plan, 1, CancellationToken.None);
        var firstMetrics = metrics.Snapshot();

        Assert.True(File.Exists(first.FilePath));
        Assert.Equal(1, renderer.RenderCalls);
        Assert.Equal(1, firstMetrics.Calls[WMWorkspaceMetricStage.Decode]);
        Assert.Equal(1, firstMetrics.Calls[WMWorkspaceMetricStage.Replay]);
        Assert.Equal(1, firstMetrics.Calls[WMWorkspaceMetricStage.Encode]);

        var second = await service.RenderAsync(plan, 2, CancellationToken.None);
        var secondMetrics = metrics.Snapshot();

        Assert.Equal(first.FilePath, second.FilePath);
        Assert.Equal(1, renderer.RenderCalls);
        Assert.Equal(firstMetrics.Calls, secondMetrics.Calls);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        try
        {
            if (templateDirectory is not null && Directory.Exists(templateDirectory))
                Directory.Delete(templateDirectory, true);
        }
        catch { }
    }

    private static void WriteSource(string path)
    {
        using var bitmap = new SKBitmap(32, 24, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(new SKColor(80, 120, 180));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private sealed class CountingTemplateRenderer : IWMTemplateRenderer
    {
        public int RenderCalls { get; private set; }

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
