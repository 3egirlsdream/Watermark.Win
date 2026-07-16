#nullable enable

using System.Security.Cryptography;
using System.Runtime.InteropServices;
using SkiaSharp;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Stages every source once, decodes the staged source once and creates the
/// reusable workspace proxy consumed by preview processors and color analysis.
/// </summary>
public sealed class WMImageImportService
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".sr2", ".raf",
        ".orf", ".rw2", ".rwl", ".pef", ".3fr", ".iiq", ".srw"
    };
    private readonly IWMSourceStager sourceStager;
    private readonly IWMPhotoMetadataReader metadataReader;
    private readonly IWMWorkspacePerformanceCounters metrics;
    private readonly IWMPhotoDecoder? photoDecoder;

    public WMImageImportService(
        IWMSourceStager sourceStager,
        IWMPhotoMetadataReader metadataReader,
        IWMWorkspacePerformanceCounters metrics,
        IWMPhotoDecoder? photoDecoder = null)
    {
        this.sourceStager = sourceStager;
        this.metadataReader = metadataReader;
        this.metrics = metrics;
        this.photoDecoder = photoDecoder;
    }

    public async Task<IReadOnlyList<WMWorkspaceMedia>> ImportAsync(
        IReadOnlyList<string> sources,
        string sessionDirectory,
        WMOperationExecutionOptions execution,
        IProgress<WMOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (sources.Count == 0) return [];

        var sourceDirectory = Path.Combine(sessionDirectory, "sources");
        var previewDirectory = Path.Combine(sessionDirectory, "previews");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(previewDirectory);
        var output = new WMWorkspaceMedia[sources.Count];
        var completed = 0;
        using var semaphore = new SemaphoreSlim(execution.MaxConcurrentImages, execution.MaxConcurrentImages);
        var tasks = sources.Select(async (source, index) =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new WMOperationProgress(
                    Volatile.Read(ref completed), sources.Count, $"正在导入 {Path.GetFileName(source)}",
                    WMOperationStage.Staging));
                var staged = await sourceStager.StageAsync(source, sourceDirectory, cancellationToken).ConfigureAwait(false);
                output[index] = await ImportOneAsync(
                    staged,
                    previewDirectory,
                    execution.PreviewMaxEdge,
                    cancellationToken).ConfigureAwait(false);
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new WMOperationProgress(
                    done, sources.Count, $"已导入 {done}/{sources.Count}",
                    done == sources.Count ? WMOperationStage.Completed : WMOperationStage.Processing));
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return output;
    }

    public async Task<IReadOnlyList<WMWorkspaceMedia>> ImportAsync(
        IReadOnlyList<IWMPhotoImportSource> sources,
        string sessionDirectory,
        WMOperationExecutionOptions execution,
        IProgress<WMOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (sources.Count == 0) return [];
        var sourceDirectory = Path.Combine(sessionDirectory, "sources");
        var previewDirectory = Path.Combine(sessionDirectory, "previews");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(previewDirectory);
        var output = new WMWorkspaceMedia[sources.Count];
        var completed = 0;
        using var semaphore = new SemaphoreSlim(execution.MaxConcurrentImages, execution.MaxConcurrentImages);
        var tasks = sources.Select(async (source, index) =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new WMOperationProgress(
                    Volatile.Read(ref completed), sources.Count, $"正在导入 {source.DisplayName}",
                    WMOperationStage.Staging));
                var extension = SafeExtension(source.DisplayName);
                var stagedPath = Path.Combine(sourceDirectory, $"{Guid.NewGuid():N}{extension}");
                try
                {
                    // Complete staging before metadata/hash/codec readers open the file.  In
                    // particular Android content streams can hold stricter sharing modes than
                    // ordinary files, so decoding while the destination writer is alive is not
                    // portable.
                    await using (var input = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false))
                    await using (var staged = new FileStream(
                                     stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                     1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await input.CopyToAsync(staged, cancellationToken).ConfigureAwait(false);
                        await staged.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }

                    output[index] = await ImportOneAsync(
                        new WMStagedSource(source.DisplayName, stagedPath, true),
                        previewDirectory,
                        execution.PreviewMaxEdge,
                        cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    try { if (File.Exists(stagedPath)) File.Delete(stagedPath); } catch { }
                    throw;
                }
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new WMOperationProgress(
                    done, sources.Count, $"已导入 {done}/{sources.Count}",
                    done == sources.Count ? WMOperationStage.Completed : WMOperationStage.Processing));
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return output;
    }

    private async Task<WMWorkspaceMedia> ImportOneAsync(
        WMStagedSource staged,
        string previewDirectory,
        int previewMaxEdge,
        CancellationToken cancellationToken)
    {
        var mediaId = Guid.NewGuid().ToString("N");
        var metadataTask = metadataReader.ReadAsync(staged.LocalPath, cancellationToken);
        string contentHash;
        await using (var stream = new FileStream(
                         staged.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            contentHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }

        var previewPath = Path.Combine(previewDirectory, $"{mediaId}.png");
        var highPrecisionDirectory = Path.Combine(
            Directory.GetParent(previewDirectory)?.FullName ?? previewDirectory,
            "artifacts",
            "source");
        var highPrecisionPath = Path.Combine(highPrecisionDirectory, $"{mediaId}.wm16");
        WMHighPrecisionArtifact? highPrecision = null;
        int width;
        int height;
        try
        {
            if (IsRaw(staged.LocalPath))
            {
                if (photoDecoder is null)
                    throw new PlatformNotSupportedException("当前宿主未注册 RAW 解码能力。");
                Directory.CreateDirectory(highPrecisionDirectory);
                WMPhotoDecodeResult decoded;
                using (metrics.Measure(WMWorkspaceMetricStage.Decode))
                {
                    decoded = await photoDecoder.DecodeAsync(
                        staged.LocalPath,
                        highPrecisionPath,
                        new WMPhotoDecodeOptions(TileHeight: 128),
                        cancellationToken).ConfigureAwait(false);
                }
                if (decoded.Status != WMImagingResultStatus.Success
                    || string.IsNullOrWhiteSpace(decoded.LinearArtifactPath)
                    || !File.Exists(decoded.LinearArtifactPath))
                    throw new PlatformNotSupportedException(decoded.Error ?? "当前设备无法解码这张 RAW 图片。");
                width = decoded.Width;
                height = decoded.Height;
                highPrecisionPath = decoded.LinearArtifactPath;
                using var reader = new WM16FileReader(highPrecisionPath);
                WriteLinearPreview(reader, previewPath, previewMaxEdge, metrics, cancellationToken);
                highPrecision = new WMHighPrecisionArtifact
                {
                    FilePath = highPrecisionPath,
                    Width = reader.Width,
                    Height = reader.Height,
                    Channels = reader.Channels,
                    ContentHash = contentHash
                };
            }
            else
            {
                using (metrics.Measure(WMWorkspaceMetricStage.Decode))
                using (var codec = SKCodec.Create(staged.LocalPath)
                                   ?? throw new InvalidOperationException($"无法读取图片：{Path.GetFileName(staged.OriginalReference)}"))
                using (var decoded = SKBitmap.Decode(codec)
                                   ?? throw new InvalidOperationException($"无法解码图片：{Path.GetFileName(staged.OriginalReference)}"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var oriented = WatermarkHelper.AutoOrient(codec, decoded);
                    using var orientedOwner = ReferenceEquals(oriented, decoded) ? null : oriented;
                    using var normalized = WMImageBitmap.NormalizeToSrgb(oriented);
                    width = normalized.Width;
                    height = normalized.Height;
                    var maximumEdge = Math.Clamp(previewMaxEdge, 320, 4096);
                    var scale = Math.Min(1f, maximumEdge / (float)Math.Max(width, height));
                    using var resized = scale < .9999f
                        ? ResizePreview(normalized, width, height, scale)
                        : normalized.Copy();

                    using (metrics.Measure(WMWorkspaceMetricStage.Encode))
                    using (var image = SKImage.FromBitmap(resized))
                    using (var data = image.Encode(SKEncodedImageFormat.Png, 100)
                                      ?? throw new InvalidOperationException("无法编码工作台预览代理。"))
                    await using (var file = new FileStream(
                                     previewPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                     256 * 1024, FileOptions.Asynchronous))
                    {
                        data.SaveTo(file);
                        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch
        {
            TryDelete(previewPath);
            TryDelete(highPrecisionPath);
            throw;
        }

        var metadataResult = await metadataTask.ConfigureAwait(false);
        var info = new FileInfo(staged.LocalPath);
        var orientation = metadataResult.Metadata.Orientation ?? 1;
        var colorSpace = metadataResult.Metadata.ColorSpace ?? "sRGB";
        var fingerprintMaterial = $"{contentHash}|{orientation}|{colorSpace}|{WMColorPipelineVersion.Current}";
        var fingerprint = new WMSourceFingerprint
        {
            StableId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprintMaterial))),
            FileIdentity = Path.GetFullPath(staged.LocalPath),
            ContentHash = contentHash,
            Length = info.Length,
            LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks,
            Orientation = orientation,
            IccHash = colorSpace,
            PipelineVersion = WMColorPipelineVersion.Current
        };
        var artifact = new WMImageArtifact
        {
            Id = Guid.NewGuid().ToString("N"),
            FilePath = staged.LocalPath,
            PreviewPath = previewPath,
            SourceOperation = WMImageOperationKind.Source,
            Width = width,
            Height = height,
            Exif = metadataResult.ToCompatibilityExifDictionary(),
            Metadata = metadataResult.Metadata,
            HighPrecision = highPrecision,
            ContentHash = contentHash,
            SourceFingerprint = fingerprint
        };
        return new WMWorkspaceMedia
        {
            Id = mediaId,
            DisplayName = Path.GetFileName(staged.OriginalReference),
            OriginalReference = staged.OriginalReference,
            Artifact = artifact
        };
    }

    private static string SafeExtension(string displayName)
    {
        var extension = Path.GetExtension(displayName).ToLowerInvariant();
        if (extension.Length is < 2 or > 10
            || extension.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '.'))
            return ".img";
        return extension;
    }

    private SKBitmap ResizePreview(SKBitmap source, int width, int height, float scale)
    {
        using (metrics.Measure(WMWorkspaceMetricStage.Scale))
        {
            return source.Resize(
                       new SKImageInfo(
                           Math.Max(1, (int)Math.Round(width * scale)),
                           Math.Max(1, (int)Math.Round(height * scale)),
                           SKColorType.Bgra8888,
                           SKAlphaType.Premul,
                           SKColorSpace.CreateSrgb()),
                       SKFilterQuality.High)
                   ?? throw new InvalidOperationException("无法生成工作台预览代理。");
        }
    }

    private static void WriteLinearPreview(
        WM16FileReader source,
        string path,
        int previewMaxEdge,
        IWMWorkspacePerformanceCounters metrics,
        CancellationToken cancellationToken)
    {
        var maximumEdge = Math.Clamp(previewMaxEdge, 320, 4096);
        var scale = Math.Min(1d, maximumEdge / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var pixels = new byte[checked(width * height * 4)];
        using (metrics.Measure(WMWorkspaceMetricStage.Scale))
        {
            var cachedTileIndex = -1;
            WMLinearTile? tile = null;
            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceY = Math.Min(source.Height - 1, (int)(y / scale));
                var tileIndex = Math.Min(source.TileCount - 1, sourceY / source.TileHeight);
                if (tileIndex != cachedTileIndex)
                {
                    tile = source.ReadTile(tileIndex, cancellationToken);
                    cachedTileIndex = tileIndex;
                }
                var localY = sourceY - tile!.RowStart;
                for (var x = 0; x < width; x++)
                {
                    var sourceX = Math.Min(source.Width - 1, (int)(x / scale));
                    var sample = (localY * source.Width + sourceX) * source.Channels;
                    var pixel = (y * width + x) * 4;
                    pixels[pixel] = ToSrgbByte(tile.Samples[sample + 2]);
                    pixels[pixel + 1] = ToSrgbByte(tile.Samples[sample + 1]);
                    pixels[pixel + 2] = ToSrgbByte(tile.Samples[sample]);
                    pixels[pixel + 3] = source.Channels == 4
                        ? (byte)(tile.Samples[sample + 3] >> 8)
                        : (byte)255;
                }
            }
        }

        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul,
            SKColorSpace.CreateSrgb()));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using (metrics.Measure(WMWorkspaceMetricStage.Encode))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100)
                          ?? throw new InvalidOperationException("无法编码 RAW 工作台预览代理。"))
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            data.SaveTo(stream);
            stream.Flush(true);
        }
    }

    private static byte ToSrgbByte(ushort sample) => (byte)Math.Clamp(
        (int)Math.Round(WMHighPrecisionImage.LinearToSrgb(sample / 65535f) * 255f),
        0,
        255);

    private static bool IsRaw(string path) => RawExtensions.Contains(Path.GetExtension(path));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
