#nullable enable

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public interface IWMPosterAssetLibrary
{
    Task<IReadOnlyList<WMPosterAssetReference>> ListAsync(
        WMPosterAssetSource source,
        string templateId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WMPosterAssetReference>> ImportFromDeviceAsync(
        CancellationToken cancellationToken = default);

    Task RememberUseAsync(
        WMPosterAssetReference asset,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns the poster asset catalog and its Blob previews. It stages picker
/// streams without decoding them; the existing template renderer remains the
/// only owner of editable preview and export decoding.
/// </summary>
public sealed class WMPosterAssetLibrary(
    IWMPhotoPicker photoPicker,
    IWMObjectUrlRegistry objectUrls,
    IWMWorkspacePerformanceCounters metrics) : IWMPosterAssetLibrary, IAsyncDisposable
{
    private const int MaximumAssetsPerSource = 60;
    private const int MaximumRecentAssets = 24;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif",
        ".tif", ".tiff", ".bmp"
    };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, WMObjectUrlLease> previewLeases = new(StringComparer.Ordinal);
    private readonly string assetRoot = ResolveAssetRoot();
    private string LibraryRoot => Path.Combine(assetRoot, "library");
    private string ThumbnailRoot => Path.Combine(assetRoot, "thumbnails");
    private string RecentPath => Path.Combine(assetRoot, "recent.json");
    private bool disposed;

    public async Task<IReadOnlyList<WMPosterAssetReference>> ListAsync(
        WMPosterAssetSource source,
        string templateId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(LibraryRoot);
        var paths = source switch
        {
            WMPosterAssetSource.Recent => await ReadRecentAsync(cancellationToken).ConfigureAwait(false),
            WMPosterAssetSource.Device => EnumerateImages(LibraryRoot),
            WMPosterAssetSource.MyAssets => EnumerateImages(LibraryRoot)
                .Concat(EnumerateImages(Global.AppPath.LogoesFolder)),
            WMPosterAssetSource.Template => EnumerateTemplateAssets(templateId),
            _ => []
        };
        return await CreateReferencesAsync(
            paths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumAssetsPerSource),
            source,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WMPosterAssetReference>> ImportFromDeviceAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Replacing a poster slot benefits from the complete camera/gallery
        // view. The platform may still use its privacy-preserving picker when
        // there is no compatible full-screen gallery application.
        var sources = await photoPicker.PickMultipleAsync(
            cancellationToken,
            WMPhotoPickerPresentation.FullScreenGallery).ConfigureAwait(false);
        if (sources.Count == 0) return [];

        Directory.CreateDirectory(LibraryRoot);
        var staged = new List<string>(sources.Count);
        var remaining = new Queue<IWMPhotoImportSource>(sources);
        try
        {
            while (remaining.TryDequeue(out var source))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var extension = ResolveExtension(source.DisplayName, source.MimeType);
                    if (!ImageExtensions.Contains(extension)) continue;

                    var temporary = Path.Combine(assetRoot, $".{Guid.NewGuid():N}{extension}.tmp");
                    Directory.CreateDirectory(assetRoot);
                    try
                    {
                        await using (var input = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false))
                        await using (var output = new FileStream(
                                         temporary,
                                         FileMode.CreateNew,
                                         FileAccess.Write,
                                         FileShare.None,
                                         1024 * 1024,
                                         FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                        }

                        string hash;
                        await using (var content = new FileStream(
                                         temporary,
                                         FileMode.Open,
                                         FileAccess.Read,
                                         FileShare.Read,
                                         1024 * 1024,
                                         FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            hash = Convert.ToHexString(
                                await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false));
                        }

                        var destination = Path.Combine(LibraryRoot, $"{hash}{extension.ToLowerInvariant()}");
                        if (File.Exists(destination)) File.Delete(temporary);
                        else File.Move(temporary, destination);
                        staged.Add(destination);
                    }
                    finally
                    {
                        TryDelete(temporary);
                    }
                }
                finally
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            while (remaining.TryDequeue(out var source))
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }

        var references = await CreateReferencesAsync(
            staged.Distinct(StringComparer.OrdinalIgnoreCase),
            WMPosterAssetSource.Device,
            cancellationToken).ConfigureAwait(false);
        foreach (var asset in references.Reverse())
            await RememberUseAsync(asset, cancellationToken).ConfigureAwait(false);
        return references;
    }

    public async Task RememberUseAsync(
        WMPosterAssetReference asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(asset.ResourcePath);
        if (!File.Exists(path) || !IsAllowedRecentPath(path)) return;

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadRecentCoreAsync(cancellationToken).ConfigureAwait(false);
            current.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
            current.Insert(0, path);
            if (current.Count > MaximumRecentAssets)
                current.RemoveRange(MaximumRecentAssets, current.Count - MaximumRecentAssets);
            Directory.CreateDirectory(assetRoot);
            var temporary = RecentPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(
                    temporary,
                    JsonSerializer.Serialize(current),
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporary, RecentPath, true);
            }
            finally
            {
                TryDelete(temporary);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        WMObjectUrlLease[] leases;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            leases = previewLeases.Values.ToArray();
            previewLeases.Clear();
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }

        foreach (var lease in leases)
            await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<WMPosterAssetReference>> CreateReferencesAsync(
        IEnumerable<string> candidates,
        WMPosterAssetSource source,
        CancellationToken cancellationToken)
    {
        var output = new List<WMPosterAssetReference>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(candidate);
            if (!File.Exists(path) || !IsImage(path)) continue;
            var info = new FileInfo(path);
            var id = StableId(path, info);
            var previewPath = await EnsureThumbnailAsync(
                id,
                path,
                cancellationToken).ConfigureAwait(false);
            var previewInfo = new FileInfo(previewPath);
            var previewUrl = await PreviewUrlAsync(
                id,
                previewPath,
                previewInfo.LastWriteTimeUtc.Ticks,
                MimeType(previewPath),
                cancellationToken).ConfigureAwait(false);
            var dimensions = await Task.Run(
                () => ReadOrientedDimensions(path),
                cancellationToken).ConfigureAwait(false);
            if (dimensions.Width <= 0 || dimensions.Height <= 0) continue;
            output.Add(new WMPosterAssetReference(
                id,
                source,
                MimeType(path),
                DisplayName(path),
                path,
                previewUrl,
                info.Length,
                dimensions.Width,
                dimensions.Height));
        }

        return output;
    }

    private static (int Width, int Height) ReadOrientedDimensions(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null) return default;
            var width = codec.Info.Width;
            var height = codec.Info.Height;
            // Keep the crop coordinate plane identical to WatermarkHelper.AutoOrient.
            // Origins other than TopLeft/BottomRight currently enter its quarter-turn
            // branch, so their displayed width and height are swapped.
            return codec.EncodedOrigin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.BottomRight
                ? (width, height)
                : (height, width);
        }
        catch
        {
            return default;
        }
    }

    private async Task<string> EnsureThumbnailAsync(
        string id,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ThumbnailRoot);
        var thumbnailPath = Path.Combine(ThumbnailRoot, $"{id}.jpg");
        if (File.Exists(thumbnailPath)) return thumbnailPath;

        var temporary = thumbnailPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var generated = await Task.Run(
                () => GenerateThumbnail(sourcePath, temporary, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (!generated) return sourcePath;
            try
            {
                File.Move(temporary, thumbnailPath, false);
            }
            catch (IOException) when (File.Exists(thumbnailPath))
            {
            }
            return File.Exists(thumbnailPath) ? thumbnailPath : sourcePath;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private bool GenerateThumbnail(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var decodeMeasurement = metrics.Measure(WMWorkspaceMetricStage.Decode);
            using var codec = SKCodec.Create(sourcePath);
            if (codec is null) return false;
            var sourceInfo = codec.Info;
            var scale = Math.Min(1f, 320f / Math.Max(sourceInfo.Width, sourceInfo.Height));
            var dimensions = codec.GetScaledDimensions(scale);
            var decodeInfo = new SKImageInfo(
                Math.Max(1, dimensions.Width),
                Math.Max(1, dimensions.Height),
                sourceInfo.ColorType,
                sourceInfo.AlphaType,
                sourceInfo.ColorSpace);
            using var decoded = SKBitmap.Decode(codec, decodeInfo);
            if (decoded is null) return false;
            var oriented = WatermarkHelper.AutoOrient(codec, decoded);
            using var orientedOwner = ReferenceEquals(oriented, decoded) ? null : oriented;
            cancellationToken.ThrowIfCancellationRequested();
            decodeMeasurement.Dispose();

            using (metrics.Measure(WMWorkspaceMetricStage.Encode))
            using (var image = SKImage.FromBitmap(oriented))
            using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 82))
            {
                if (data is null) return false;
                using var output = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.SequentialScan);
                data.SaveTo(output);
                output.Flush();
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> PreviewUrlAsync(
        string id,
        string path,
        long version,
        string mimeType,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (previewLeases.TryGetValue(id, out var existing))
                return existing.Url;
        }
        finally
        {
            gate.Release();
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var lease = await objectUrls.PublishAsync(
            $"poster-asset:{id}",
            Math.Max(0, version),
            stream,
            mimeType,
            cancellationToken).ConfigureAwait(false);
        if (lease is null) return string.Empty;

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (previewLeases.TryGetValue(id, out var existing))
            {
                await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
                return existing.Url;
            }
            previewLeases[id] = lease;
            return lease.Url;
        }
        finally
        {
            gate.Release();
        }
    }

    private IEnumerable<string> EnumerateTemplateAssets(string templateId)
    {
        if (!IsSafeSegment(templateId)) return [];
        return EnumerateImages(Path.Combine(Global.AppPath.TemplatesFolder, templateId));
    }

    private static IEnumerable<string> EnumerateImages(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return [];
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(IsImage)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> ReadRecentAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadRecentCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<string>> ReadRecentCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(RecentPath)) return [];
            var json = await File.ReadAllTextAsync(RecentPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<string>>(json)?
                .Where(path => !string.IsNullOrWhiteSpace(path)
                               && File.Exists(path)
                               && IsAllowedRecentPath(Path.GetFullPath(path))
                               && IsImage(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumRecentAssets)
                .ToList() ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private bool IsAllowedRecentPath(string path)
    {
        var roots = new[]
        {
            assetRoot,
            Global.AppPath.LogoesFolder,
            Global.AppPath.TemplatesFolder
        };
        return roots.Any(root => IsDescendant(path, root));
    }

    private static bool IsDescendant(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveExtension(string displayName, string? mimeType)
    {
        var extension = Path.GetExtension(displayName);
        if (ImageExtensions.Contains(extension)) return extension;
        return mimeType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            "image/heif" => ".heif",
            "image/tiff" => ".tiff",
            "image/bmp" => ".bmp",
            _ => ".jpg"
        };
    }

    private static string StableId(string path, FileInfo info)
    {
        var value = $"{path}\n{info.Length}\n{info.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string DisplayName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Length <= 20) return name;
        return $"{name[..10]}…{name[^6..]}";
    }

    private static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));

    private static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".heif" => "image/heif",
        ".tif" or ".tiff" => "image/tiff",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };

    private static bool IsSafeSegment(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value is not "." and not ".."
        && !Path.IsPathRooted(value)
        && !value.Contains('/')
        && !value.Contains('\\')
        && string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal);

    private static string ResolveAssetRoot()
    {
        var basePath = OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                WMAppPath.AppId)
            : Global.AppPath.BasePath;
        return Path.Combine(basePath, "PosterAssets");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
