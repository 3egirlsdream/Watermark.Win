using SkiaSharp;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMTemplateOperationProcessorTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "watermark-template-processor-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAsync_ReusesPreparedCurrentImageAndRendersOtherTargetOnce()
    {
        Directory.CreateDirectory(root);
        var sourcePreview = Path.Combine(root, "source-preview.png");
        var preparedPath = Path.Combine(root, "prepared.png");
        WriteImage(sourcePreview, SKColors.SteelBlue);
        WriteImage(preparedPath, SKColors.Orange);
        var first = CreateInput("first", sourcePreview);
        var second = CreateInput("second", sourcePreview);
        var settings = new WMTemplateOperationSettings(new WMCanvas());
        var prepared = new WMImageArtifact
        {
            Id = "prepared-first",
            FilePath = preparedPath,
            PreviewPath = preparedPath,
            ParentArtifactIds = [first.Id],
            SourceOperation = WMImageOperationKind.Template,
            Width = 8,
            Height = 8,
            CanvasSnapshotJson = settings.CanvasJson,
            ContentHash = "prepared-content"
        };
        var renderer = new CountingPreviewRenderer(File.ReadAllBytes(sourcePreview));
        using var scheduler = new WMProcessingScheduler();
        var processor = new WMTemplateOperationProcessor(renderer, scheduler);

        var result = await processor.ExecuteAsync(new WMOperationRequest(
                [first, second],
                settings,
                true,
                Path.Combine(root, "outputs"),
                Execution: new WMOperationExecutionOptions
                {
                    MaxConcurrentImages = 2,
                    MaxPixelWorkers = 1,
                    PreviewMaxEdge = 256
                }),
            new Dictionary<string, WMImageArtifact> { [first.Id] = prepared });

        Assert.Equal(1, renderer.RenderCount);
        Assert.Equal(preparedPath, result.Outputs[0].FilePath);
        Assert.Equal(sourcePreview, renderer.LastSourcePath);
        Assert.Equal(2, result.Outputs.Count);
    }

    private static WMImageArtifact CreateInput(string id, string previewPath) => new()
    {
        Id = id,
        FilePath = Path.Combine(Path.GetDirectoryName(previewPath)!, $"missing-{id}.png"),
        PreviewPath = previewPath,
        SourceOperation = WMImageOperationKind.Source,
        Width = 8,
        Height = 8,
        ContentHash = $"{id}-content"
    };

    private static void WriteImage(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(8, 8);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }

    private sealed class CountingPreviewRenderer(byte[] output) : IWMTemplateRenderer
    {
        public int RenderCount { get; private set; }
        public string? LastSourcePath { get; private set; }

        public Task<byte[]> RenderAsync(WMTemplateRenderRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderCount++;
            LastSourcePath = request.Canvas.Path;
            return Task.FromResult(output);
        }

        public SKBitmap RenderBitmap(WMCanvas canvas, SKBitmap source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
