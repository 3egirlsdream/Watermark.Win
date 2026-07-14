#nullable enable
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac;

public sealed record MacImportedImage(
    string Id,
    string SourcePath,
    string ThumbnailPath,
    IReadOnlyDictionary<string, string> Exif,
    WMColorReferenceProfile? ColorProfile = null);

public sealed class MacImageImportService
{
    private readonly IWMColorAnalysisService colorAnalysisService;

    public MacImageImportService(IWMColorAnalysisService? colorAnalysisService = null)
    {
        this.colorAnalysisService = colorAnalysisService ?? new WMColorAnalysisService();
    }

    public async Task<IReadOnlyList<MacImportedImage>> ImportAsync(
        IReadOnlyList<string> files,
        IProgress<WMOperationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int previewMaxEdge = 2048)
    {
        if (files.Count == 0) return [];
        Directory.CreateDirectory(Global.AppPath.ThumbnailFolder);
        var results = new MacImportedImage[files.Count];
        var completed = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var sourcePath = files[index];
                progress?.Report(new WMOperationProgress(
                    Volatile.Read(ref completed), files.Count, $"正在读取 {Path.GetFileName(sourcePath)}",
                    WMOperationStage.Decoding));
                var id = Guid.NewGuid().ToString("N");
                var thumbnailName = CreateThumbnailName(sourcePath, id);
                var thumbnailPath = Path.Combine(Global.AppPath.ThumbnailFolder, thumbnailName);
                var exifTask = ExifHelper.ReadImageAsync(sourcePath);
                await Task.Run(() => WriteSrgbPreview(sourcePath, thumbnailPath, previewMaxEdge), token);
                var colorProfile = await Task.Run(() => colorAnalysisService.Analyze(sourcePath, token), token);
                var exif = await exifTask.WaitAsync(token);
                results[index] = new MacImportedImage(id, sourcePath, thumbnailPath, exif, colorProfile);
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new WMOperationProgress(
                    done, files.Count, $"已导入 {done}/{files.Count}",
                    done == files.Count ? WMOperationStage.Completed : WMOperationStage.Processing));
            });

        return results;
    }

    private static string CreateThumbnailName(string path, string id)
    {
        var stamp = File.GetLastWriteTimeUtc(path).Ticks;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Path.GetFullPath(path)}:{stamp}")))[..12];
        return $"{hash}_{id}.png";
    }

    private static void WriteSrgbPreview(string sourcePath, string targetPath, int previewMaxEdge)
    {
        using var codec = SKCodec.Create(sourcePath)
            ?? throw new InvalidOperationException($"无法读取图片：{Path.GetFileName(sourcePath)}");
        using var decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException($"无法解码图片：{Path.GetFileName(sourcePath)}");
        var oriented = WatermarkHelper.AutoOrient(codec, decoded);
        using var orientedOwner = ReferenceEquals(oriented, decoded) ? null : oriented;
        using var srgb = new SKBitmap(new SKImageInfo(
            oriented.Width, oriented.Height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        using (var canvas = new SKCanvas(srgb))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(oriented, 0, 0);
            canvas.Flush();
        }
        var maximumEdge = Math.Clamp(previewMaxEdge, 1600, 2560);
        var edge = Math.Max(srgb.Width, srgb.Height);
        var scale = Math.Min(1f, maximumEdge / (float)edge);
        using var resized = scale < 0.9999f
            ? srgb.Resize(new SKImageInfo(
                Math.Max(1, (int)Math.Round(srgb.Width * scale)),
                Math.Max(1, (int)Math.Round(srgb.Height * scale)),
                SKColorType.Bgra8888,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgb()), SKFilterQuality.High)
            : srgb.Copy();
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("无法编码sRGB预览图。");
        File.WriteAllBytes(targetPath, data.ToArray());
    }
}
