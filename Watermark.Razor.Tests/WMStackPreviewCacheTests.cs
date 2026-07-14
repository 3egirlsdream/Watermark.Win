using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMStackPreviewCacheTests
{
    [Fact]
    public void Wmpv1_RoundTripsRowsMaskFingerprintAndCrc()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "frame.wmpv");
        try
        {
            var samples = Enumerable.Range(0, 5 * 3 * 4).Select(value => (ushort)(value * 31)).ToArray();
            var mask = Enumerable.Range(0, 15).Select(value => value == 4 ? (byte)0 : (byte)255).ToArray();
            using (var writer = new WMPV1Writer(path, 5, 3, 4, 16, "source-fingerprint"))
            {
                writer.WriteTile(new WMLinearTile(0, 3, 5, 4, samples, mask));
                writer.Complete();
            }
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            using var reader = new WMPV1Reader(path);
            Assert.Equal("source-fingerprint", reader.Fingerprint);
            Assert.Equal(samples, reader.ReadRows(0, 3).Samples);
            Assert.Equal(mask, reader.ReadRows(0, 3).ValidityMask);
            var directSamples = new ushort[samples.Length + 4];
            var directMask = new byte[mask.Length + 2];
            reader.ReadRowsInto(0, 3, directSamples, 2, directMask, 1);
            Assert.Equal(samples, directSamples.AsSpan(2, samples.Length).ToArray());
            Assert.Equal(mask, directMask.AsSpan(1, mask.Length).ToArray());
            Assert.True(reader.ValidateCrc());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Preview_ReusesDecodeCacheWithoutRehashingSources()
    {
        var directory = CreateDirectory();
        try
        {
            var first = Path.Combine(directory, "first.png");
            var second = Path.Combine(directory, "second.png");
            WritePng(first, new SKColor(16, 24, 48), 640, 400);
            WritePng(second, new SKColor(32, 48, 80), 640, 400);
            var inputs = new[] { Artifact("first", first), Artifact("second", second) };
            var settings = WMMultiFrameStackSettings.CreateDefault(WMStackMode.StarTrail);
            var work = Path.Combine(directory, "work");
            var engine = new WMMultiFramePreviewEngine();
            var execution = new WMOperationExecutionOptions
            {
                PreviewRenderMaxEdge = 320,
                PreviewAnalysisMaxEdge = 320,
                PreviewDecodeConcurrency = 2,
                MaxPixelWorkers = 1
            };

            var firstResult = await engine.ExecuteAsync(new WMOperationRequest(inputs, settings, true, work,
                Execution: execution), settings);
            Assert.True(File.Exists(Assert.Single(firstResult.Outputs).FilePath));

            var changed = settings with { Reduction = WMReductionMode.Lighten, AutomaticReduction = false };
            await engine.ExecuteAsync(new WMOperationRequest(inputs, changed, true, work,
                Execution: execution), changed);

            Assert.Equal(2, Directory.GetFiles(Path.Combine(work, "wmpv-v1", "decoded"), "*.wmpv").Length);
            var metricsPath = Directory.GetFiles(Path.Combine(work, "wmpv-v1", "final"), "*.metrics.json")
                .OrderBy(File.GetLastWriteTimeUtc).Last();
            var metrics = JsonSerializer.Deserialize<WMStackPerformanceMetrics>(await File.ReadAllTextAsync(metricsPath));
            Assert.NotNull(metrics);
            Assert.Equal(2, metrics.DecodeCacheHits);
            Assert.Equal(0, metrics.SourceBytesRead);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static WMImageArtifact Artifact(string id, string path)
    {
        using var stream = File.OpenRead(path);
        return new WMImageArtifact
        {
            Id = id,
            FilePath = path,
            Width = 640,
            Height = 400,
            ContentHash = Convert.ToHexString(SHA256.HashData(stream))
        };
    }

    private static void WritePng(string path, SKColor color, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wmpv-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
