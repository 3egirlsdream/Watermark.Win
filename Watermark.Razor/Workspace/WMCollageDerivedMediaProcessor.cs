#nullable enable

using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Creates a stable multi-source collage artifact. Source dimensions are read
/// without decoding; each source is then decoded exactly once and the final
/// PNG is encoded exactly once.
/// </summary>
public sealed class WMCollageDerivedMediaProcessor(
    IWMArtifactCache cache,
    IWMExecutionProfileProvider executionProfiles,
    IWMWorkspacePerformanceCounters metrics) : IWMDerivedMediaProcessor
{
    private const int PipelineVersion = 1;

    public async Task<WMDerivedMediaOutput> ExecuteAsync(
        WMDerivedMediaRequest request,
        IReadOnlyList<WMImageArtifact> inputs,
        string sessionDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(inputs);
        if (request.Kind != WMDerivedMediaKind.Collage)
            throw new NotSupportedException($"不支持派生操作 {request.Kind}。");
        if (inputs.Count < 2)
            throw new InvalidOperationException("拼图至少需要两张素材。");
        if (request.Collage.SourceMediaIds.Count != inputs.Count)
            throw new InvalidOperationException("拼图素材与操作参数不一致。");

        var settings = request.Collage with
        {
            GapPixels = Math.Clamp(request.Collage.GapPixels, 0, 512),
            BackgroundColor = NormalizeColor(request.Collage.BackgroundColor)
        };
        var fingerprint = CreateFingerprint(inputs, settings);
        var cached = await cache.TryGetAsync(sessionDirectory, fingerprint, cancellationToken)
            .ConfigureAwait(false);
        var outputPath = cached?.FilePath;
        var dimensions = ReadDimensions(inputs);
        var (width, height) = CalculateCanvas(dimensions, settings.Direction, settings.GapPixels);
        ValidateBudget(width, height, executionProfiles.GetInteractiveProfile().MemoryBudgetBytes);

        if (outputPath is null)
        {
            var outputDirectory = Path.Combine(sessionDirectory, "artifacts", "derived");
            Directory.CreateDirectory(outputDirectory);
            outputPath = Path.Combine(outputDirectory, $"collage-{fingerprint[..24].ToLowerInvariant()}.png");
            var temporary = outputPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using var canvasBitmap = new SKBitmap(
                    new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
                using var canvas = new SKCanvas(canvasBitmap);
                canvas.Clear(SKColor.Parse(settings.BackgroundColor));
                var offset = 0;
                for (var index = 0; index < inputs.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var decoded = DecodeOriented(inputs[index].FilePath);
                    var destination = settings.Direction == WMCollageDirection.Horizontal
                        ? new SKRect(offset, 0, offset + decoded.Width, decoded.Height)
                        : new SKRect(0, offset, decoded.Width, offset + decoded.Height);
                    canvas.DrawBitmap(decoded, destination);
                    offset += (settings.Direction == WMCollageDirection.Horizontal
                                  ? decoded.Width
                                  : decoded.Height)
                              + settings.GapPixels;
                }
                canvas.Flush();
                using (metrics.Measure(WMWorkspaceMetricStage.Encode))
                using (var image = SKImage.FromBitmap(canvasBitmap))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100)
                                  ?? throw new InvalidOperationException("无法编码拼图产物。"))
                await using (var stream = new FileStream(
                                 temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    data.SaveTo(stream);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temporary, outputPath, true);
                await cache.CommitAsync(
                    sessionDirectory,
                    fingerprint,
                    outputPath,
                    executionProfiles.GetInteractiveProfile().PreviewCacheBudgetBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        var artifactId = Guid.NewGuid().ToString("N");
        var operation = WMImageOperation.Create(
            WMImageOperationKind.Collage,
            inputs.Select(item => item.Id),
            [artifactId],
            settings);
        var artifact = new WMImageArtifact
        {
            Id = artifactId,
            FilePath = outputPath,
            PreviewPath = outputPath,
            ParentArtifactIds = inputs.Select(item => item.Id).ToArray(),
            SourceOperation = WMImageOperationKind.Collage,
            OperationId = operation.Id,
            ContentHash = fingerprint,
            Width = width,
            Height = height,
            ColorSpace = "sRGB"
        };
        return new WMDerivedMediaOutput(
            artifact,
            operation,
            request.SuggestedFileName ?? $"拼图-{DateTime.Now:yyyyMMdd-HHmmss}.png");
    }

    private IReadOnlyList<(int Width, int Height)> ReadDimensions(IReadOnlyList<WMImageArtifact> inputs)
    {
        var result = new List<(int Width, int Height)>(inputs.Count);
        foreach (var input in inputs)
        {
            using var codec = SKCodec.Create(input.FilePath)
                              ?? throw new InvalidDataException($"无法读取素材：{Path.GetFileName(input.FilePath)}");
            var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop
                or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom
                or SKEncodedOrigin.LeftBottom;
            result.Add(swap ? (codec.Info.Height, codec.Info.Width) : (codec.Info.Width, codec.Info.Height));
        }
        return result;
    }

    private SKBitmap DecodeOriented(string path)
    {
        using (metrics.Measure(WMWorkspaceMetricStage.Decode))
        using (var codec = SKCodec.Create(path)
                           ?? throw new InvalidDataException($"无法读取素材：{Path.GetFileName(path)}"))
        {
            var bitmap = SKBitmap.Decode(codec)
                         ?? throw new InvalidDataException($"无法解码素材：{Path.GetFileName(path)}");
            if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft) return bitmap;
            var oriented = ApplyOrientation(bitmap, codec.EncodedOrigin);
            bitmap.Dispose();
            return oriented;
        }
    }

    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;
        var target = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(target);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(target.Width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(target.Width, target.Height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, target.Height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(target.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(target.Width, target.Height); canvas.RotateDegrees(-90); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, target.Height); canvas.RotateDegrees(-90); canvas.Scale(1, -1); break;
        }
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return target;
    }

    private static (int Width, int Height) CalculateCanvas(
        IReadOnlyList<(int Width, int Height)> dimensions,
        WMCollageDirection direction,
        int gap)
    {
        checked
        {
            return direction == WMCollageDirection.Horizontal
                ? (dimensions.Sum(item => item.Width) + gap * (dimensions.Count - 1),
                    dimensions.Max(item => item.Height))
                : (dimensions.Max(item => item.Width),
                    dimensions.Sum(item => item.Height) + gap * (dimensions.Count - 1));
        }
    }

    private static void ValidateBudget(int width, int height, long memoryBudgetBytes)
    {
        var required = checked((long)width * height * 4L);
        if (width <= 0 || height <= 0 || width > 65535 || height > 65535)
            throw new InvalidOperationException("拼图尺寸超出当前图像后端限制。");
        if (required > Math.Max(64L * 1024 * 1024, memoryBudgetBytes * 7 / 10))
            throw new InvalidOperationException("拼图预计内存超过当前执行预算的70%，请减少素材或先缩小图片。");
    }

    private static string CreateFingerprint(
        IReadOnlyList<WMImageArtifact> inputs,
        WMCollageSettings settings)
    {
        var builder = new StringBuilder($"wm-collage-v{PipelineVersion}|{settings.Direction}|{settings.GapPixels}|{settings.BackgroundColor}");
        foreach (var input in inputs)
        {
            var info = new FileInfo(input.FilePath);
            builder.Append('|')
                .Append(input.ContentHash ?? input.SourceFingerprint?.StableId ?? input.Id)
                .Append(':').Append(info.Exists ? info.Length : 0)
                .Append(':').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string NormalizeColor(string value)
    {
        try { return SKColor.Parse(value).ToString(); }
        catch { return "#FFFFFFFF"; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
