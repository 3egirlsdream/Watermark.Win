using SkiaSharp;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMCropPipelineTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"watermark-crop-pipeline-{Guid.NewGuid():N}");

    [Fact]
    public async Task Compiler_OrdersCropBeforeTemplateAndColor_AndIncludesItInFingerprint()
    {
        var artifact = Artifact("/tmp/source.jpg", 1200, 800);
        var media = Media(artifact);
        var canvas = new WMCanvas { ID = "crop-template", Name = "crop-template" };
        var recipe = new WMColorRecipe { Name = "crop-grade" };
        var crop = new WMCropSettings
        {
            VisibleWidth = .75,
            VisibleHeight = .75,
            AspectPreset = WMCropAspectPreset.Free
        };
        var session = new WMWorkspaceSession
        {
            Id = "crop-order",
            Media = [media],
            MediaCatalog = [media],
            ActiveMediaIds = [media.Id],
            CurrentMediaId = media.Id,
            TemplateId = canvas.ID,
            ColorRecipe = recipe,
            CropSettingsByMediaId = new Dictionary<string, WMCropSettings> { [media.Id] = crop },
            Operations =
            [
                WMImageOperation.Create(WMImageOperationKind.Template, [artifact.Id], ["template"],
                    new WMWorkspaceTemplateSelection(canvas.ID, Global.CanvasSerialize(canvas))),
                WMImageOperation.Create(WMImageOperationKind.ColorGrade, [artifact.Id], ["grade"], recipe)
            ]
        };
        var compiler = new WMRenderPlanCompiler();

        var cropped = await compiler.CompileAsync(
            session, media.Id, WMRenderTarget.SettledPreview(), CancellationToken.None);
        var identity = await compiler.CompileAsync(
            session with { CropSettingsByMediaId = new Dictionary<string, WMCropSettings>() },
            media.Id,
            WMRenderTarget.SettledPreview(),
            CancellationToken.None);

        Assert.Equal(
            [WMImageOperationKind.Crop, WMImageOperationKind.Template, WMImageOperationKind.ColorGrade],
            cropped.Steps.Select(step => step.Operation.Kind));
        Assert.NotEqual(identity.GraphFingerprint, cropped.GraphFingerprint);
    }

    [Fact]
    public async Task PreviewCrop_UsesOneDecodeReplayCropAndEncode_ThenHitsCache()
    {
        var previewDirectory = Path.Combine(root, "session", "previews");
        Directory.CreateDirectory(previewDirectory);
        var sourcePath = Path.Combine(previewDirectory, "source.png");
        using (var bitmap = new SKBitmap(120, 80, SKColorType.Bgra8888, SKAlphaType.Premul))
        {
            bitmap.Erase(new SKColor(20, 120, 190));
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(sourcePath);
            data.SaveTo(stream);
        }
        var artifact = Artifact(sourcePath, 120, 80);
        var media = Media(artifact);
        var session = new WMWorkspaceSession
        {
            Id = "crop-preview",
            Media = [media],
            MediaCatalog = [media],
            ActiveMediaIds = [media.Id],
            CurrentMediaId = media.Id,
            CropSettingsByMediaId = new Dictionary<string, WMCropSettings>
            {
                [media.Id] = new()
                {
                    VisibleWidth = .5,
                    VisibleHeight = .5,
                    AspectPreset = WMCropAspectPreset.Free
                }
            }
        };
        var metrics = new WMWorkspacePerformanceCounters();
        var scheduler = new WMProcessingScheduler();
        var service = new WMWorkspacePreviewService(
            new WMTemplateRenderer(new WatermarkHelper()),
            new WMColorGradeOperationProcessor(scheduler),
            scheduler,
            new TestProfiles(),
            metrics);
        var plan = await new WMRenderPlanCompiler().CompileAsync(
            session, media.Id, WMRenderTarget.SettledPreview(), CancellationToken.None);

        var first = await service.RenderAsync(plan, 1, CancellationToken.None);
        var firstMetrics = metrics.Snapshot();
        var second = await service.RenderAsync(plan, 2, CancellationToken.None);

        Assert.Equal(66, first.Width);
        Assert.Equal(44, first.Height);
        Assert.Equal(1, firstMetrics.Calls[WMWorkspaceMetricStage.Decode]);
        Assert.Equal(1, firstMetrics.Calls[WMWorkspaceMetricStage.Replay]);
        Assert.Equal(1, firstMetrics.Calls[WMWorkspaceMetricStage.Crop]);
        Assert.Equal(1, firstMetrics.Calls[WMWorkspaceMetricStage.Encode]);
        Assert.True(second.CacheHit);
        Assert.Equal(firstMetrics.Calls, metrics.Snapshot().Calls);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }

    private static WMImageArtifact Artifact(string path, int width, int height) => new()
    {
        Id = "crop-artifact",
        FilePath = path,
        PreviewPath = path,
        ContentHash = "crop-content",
        Width = width,
        Height = height
    };

    private static WMWorkspaceMedia Media(WMImageArtifact artifact) => new()
    {
        Id = "crop-media",
        DisplayName = "crop.png",
        OriginalReference = artifact.FilePath,
        Artifact = artifact
    };

    private sealed class TestProfiles : IWMExecutionProfileProvider
    {
        public WMOperationExecutionOptions GetInteractiveProfile() => new()
        {
            PreviewMaxEdge = 1600,
            PreviewRenderMaxEdge = 1600,
            MemoryBudgetBytes = 512L * 1024 * 1024,
            PreviewCacheBudgetBytes = 256L * 1024 * 1024,
            MaxConcurrentImages = 1,
            MaxPixelWorkers = 1
        };

        public WMImagingCapabilities GetImagingCapabilities() => WMImagingCapabilities.MobileDisabled;
    }
}
