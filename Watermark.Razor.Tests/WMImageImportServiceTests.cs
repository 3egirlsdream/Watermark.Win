using SkiaSharp;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMImageImportServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "watermark-raw-import-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RawImport_DecodesOnce_AndBuildsProxyFromSameHighPrecisionArtifact()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.dng");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5]);
        var metrics = new WMWorkspacePerformanceCounters();
        var decoder = new FakeRawDecoder();
        var importer = new WMImageImportService(
            new WMLocalSourceStager(copyLocalSources: true),
            new WMMetadataExtractorReader(),
            metrics,
            decoder);

        var imported = await importer.ImportAsync(
            [source],
            Path.Combine(root, "session"),
            new WMOperationExecutionOptions
            {
                MaxConcurrentImages = 1,
                PreviewMaxEdge = 1600
            });

        var media = Assert.Single(imported);
        Assert.Equal(1, decoder.DecodeCalls);
        Assert.NotNull(media.Artifact.HighPrecision);
        Assert.True(File.Exists(media.Artifact.HighPrecision!.FilePath));
        Assert.True(File.Exists(media.Artifact.PreviewPath));
        using var codec = SKCodec.Create(media.Artifact.PreviewPath);
        Assert.NotNull(codec);
        Assert.Equal(8, codec!.Info.Width);
        Assert.Equal(4, codec.Info.Height);
        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.Decode]);
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.Scale]);
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.Encode]);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }

    private sealed class FakeRawDecoder : IWMPhotoDecoder
    {
        public int DecodeCalls { get; private set; }

        public bool CanDecode(string path) =>
            string.Equals(Path.GetExtension(path), ".dng", StringComparison.OrdinalIgnoreCase);

        public Task<WMPhotoDecodeResult> DecodeAsync(
            string sourcePath,
            string outputPath,
            WMPhotoDecodeOptions decodeOptions,
            CancellationToken cancellationToken = default)
        {
            DecodeCalls++;
            const int width = 8;
            const int height = 4;
            var samples = new ushort[width * height * 4];
            for (var index = 0; index < samples.Length; index += 4)
            {
                samples[index] = 16_384;
                samples[index + 1] = 32_768;
                samples[index + 2] = 49_152;
                samples[index + 3] = ushort.MaxValue;
            }
            using (var writer = new WM16FileWriter(outputPath, width, height, 4, 16))
            {
                writer.WriteTile(new WMLinearTile(0, height, width, 4, samples), cancellationToken);
                writer.Complete();
            }
            return Task.FromResult(new WMPhotoDecodeResult(
                WMImagingResultStatus.Success,
                outputPath,
                width,
                height,
                "test-raw",
                "1"));
        }
    }
}
