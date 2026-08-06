using SkiaSharp;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMCollageDerivedMediaProcessorTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "watermark-collage-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HorizontalCollage_DecodesEachSourceOnceAndEncodesOnce()
    {
        var red = CreateImage("red.png", 10, 20, SKColors.Red);
        var blue = CreateImage("blue.png", 30, 10, SKColors.Blue);
        var metrics = new WMWorkspacePerformanceCounters();
        var processor = CreateProcessor(metrics);
        var request = Request(WMCollageDirection.Horizontal);

        var result = await processor.ExecuteAsync(
            request,
            [Artifact("red", red), Artifact("blue", blue)],
            root);

        Assert.Equal(40, result.Artifact.Width);
        Assert.Equal(20, result.Artifact.Height);
        using var output = SKBitmap.Decode(result.Artifact.FilePath);
        Assert.Equal(SKColors.Red, output.GetPixel(5, 5));
        Assert.Equal(SKColors.Blue, output.GetPixel(20, 5));
        Assert.Equal(SKColors.White, output.GetPixel(20, 15));
        var snapshot = metrics.Snapshot();
        Assert.Equal(2, snapshot.Calls[WMWorkspaceMetricStage.Decode]);
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.Encode]);
    }

    [Fact]
    public async Task SameFingerprint_ReusesStableFileWithoutDecodeOrEncode()
    {
        var firstPath = CreateImage("first.png", 10, 20, SKColors.Red);
        var secondPath = CreateImage("second.png", 30, 10, SKColors.Blue);
        var metrics = new WMWorkspacePerformanceCounters();
        var processor = CreateProcessor(metrics);
        var inputs = new[] { Artifact("first", firstPath), Artifact("second", secondPath) };
        var request = Request(WMCollageDirection.Vertical);

        var first = await processor.ExecuteAsync(request, inputs, root);
        var before = metrics.Snapshot();
        var second = await processor.ExecuteAsync(request, inputs, root);
        var after = metrics.Snapshot();

        Assert.Equal(first.Artifact.FilePath, second.Artifact.FilePath);
        Assert.Equal(before.Calls[WMWorkspaceMetricStage.Decode], after.Calls[WMWorkspaceMetricStage.Decode]);
        Assert.Equal(before.Calls[WMWorkspaceMetricStage.Encode], after.Calls[WMWorkspaceMetricStage.Encode]);
    }

    [Fact]
    public async Task PosterApplication_MapsOnlyDeclaredPhotoSlotAndInvokesSharedRendererOnce()
    {
        var sourcePath = CreateImage("slot.png", 12, 8, SKColors.Green);
        var canvas = new WMCanvas
        {
            ID = "poster-template",
            Name = "poster",
            CanvasSizing = new WMCanvasSizing
            {
                Mode = WMCanvasSizingMode.Fixed,
                ReferenceWidth = 30,
                ReferenceHeight = 20
            }
        };
        var replaceable = new WMContainer { ID = "replaceable", Path = "placeholder.jpg" };
        var fixedLayer = new WMContainer { ID = "fixed", Path = "fixed.jpg" };
        canvas.Children.Add(replaceable);
        canvas.Children.Add(fixedLayer);
        var slot = WMPosterAssetSlots.Ensure(canvas, replaceable);
        slot.Purpose = WMPosterAssetPurpose.Photo;
        var renderer = new RecordingTemplateRenderer(CreatePngBytes(30, 20));
        var metrics = new WMWorkspacePerformanceCounters();
        var processor = new WMPosterApplicationProcessor(
            renderer, new WMArtifactCache(), new TestProfiles(), metrics);
        var artifact = Artifact("slot", sourcePath);
        var plan = WMPosterApplicationPlanner.FromTemplateFirst(canvas, [artifact.Id]);

        var results = await processor.ExecuteAsync(
            plan,
            new Dictionary<string, WMImageArtifact> { [artifact.Id] = artifact },
            root);

        var result = Assert.Single(results);
        Assert.Equal(1, renderer.RenderCalls);
        Assert.Equal(sourcePath, Assert.IsType<WMContainer>(renderer.RenderedCanvas!.Children[0]).Path);
        Assert.Equal("fixed.jpg", Assert.IsType<WMContainer>(renderer.RenderedCanvas.Children[1]).Path);
        Assert.Equal(30, result.Artifact.Width);
        Assert.Equal(20, result.Artifact.Height);
        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.Replay]);
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.Encode]);
    }

    [Fact]
    public async Task PosterApplication_SameInstanceAndAssetsReuseRenderedArtifact()
    {
        var sourcePath = CreateImage("poster-cache.png", 12, 8, SKColors.Green);
        var canvas = new WMCanvas
        {
            ID = "poster-cache-template",
            Name = "poster cache",
            CanvasSizing = new WMCanvasSizing
            {
                Mode = WMCanvasSizingMode.Fixed,
                ReferenceWidth = 30,
                ReferenceHeight = 20
            }
        };
        var owner = new WMContainer { ID = "photo" };
        canvas.Children.Add(owner);
        WMPosterAssetSlots.Ensure(canvas, owner).Purpose = WMPosterAssetPurpose.Photo;
        var renderer = new RecordingTemplateRenderer(CreatePngBytes(30, 20));
        var metrics = new WMWorkspacePerformanceCounters();
        var processor = new WMPosterApplicationProcessor(
            renderer, new WMArtifactCache(), new TestProfiles(), metrics);
        var artifact = Artifact("photo", sourcePath);
        var plan = WMPosterApplicationPlanner.FromTemplateFirst(canvas, [artifact.Id]);
        var inputs = new Dictionary<string, WMImageArtifact> { [artifact.Id] = artifact };

        var first = Assert.Single(await processor.ExecuteAsync(plan, inputs, root));
        var before = metrics.Snapshot();
        var second = Assert.Single(await processor.ExecuteAsync(plan, inputs, root));
        var after = metrics.Snapshot();

        Assert.Equal(first.Artifact.FilePath, second.Artifact.FilePath);
        Assert.Equal(1, renderer.RenderCalls);
        Assert.Equal(
            before.Calls[WMWorkspaceMetricStage.Replay],
            after.Calls[WMWorkspaceMetricStage.Replay]);
        Assert.Equal(
            before.Calls[WMWorkspaceMetricStage.Encode],
            after.Calls[WMWorkspaceMetricStage.Encode]);
    }

    private WMCollageDerivedMediaProcessor CreateProcessor(IWMWorkspacePerformanceCounters metrics) =>
        new(new WMArtifactCache(), new TestProfiles(), metrics);

    private static WMDerivedMediaRequest Request(WMCollageDirection direction) => new(
        WMDerivedMediaKind.Collage,
        ["media-first", "media-second"],
        "创建拼图",
        new WMCollageSettings(["media-first", "media-second"], direction));

    private static WMImageArtifact Artifact(string id, string path) => new()
    {
        Id = id,
        FilePath = path,
        PreviewPath = path,
        ContentHash = id,
        Width = 1,
        Height = 1
    };

    private string CreateImage(string name, int width, int height, SKColor color)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static byte[] CreatePngBytes(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }

    private sealed class TestProfiles : IWMExecutionProfileProvider
    {
        public WMOperationExecutionOptions GetInteractiveProfile() => new()
        {
            MaxConcurrentImages = 1,
            PreviewMaxEdge = 1600,
            PreviewDecodeConcurrency = 1,
            MemoryBudgetBytes = 256L * 1024 * 1024,
            PreviewCacheBudgetBytes = 128L * 1024 * 1024
        };

        public WMImagingCapabilities GetImagingCapabilities() => WMImagingCapabilities.MobileDisabled;
    }

    private sealed class RecordingTemplateRenderer(byte[] output) : IWMTemplateRenderer
    {
        public int RenderCalls { get; private set; }
        public WMCanvas? RenderedCanvas { get; private set; }

        public Task<byte[]> RenderAsync(
            WMTemplateRenderRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderCalls++;
            RenderedCanvas = Global.ReadConfig(Global.CanvasSerialize(request.Canvas));
            return Task.FromResult(output);
        }

        public SKBitmap RenderBitmap(
            WMCanvas canvas,
            SKBitmap source,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
