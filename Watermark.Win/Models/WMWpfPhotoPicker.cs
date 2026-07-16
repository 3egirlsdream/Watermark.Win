#nullable enable

using System.IO;
using Watermark.Razor.Workspace;

namespace Watermark.Win.Models;

/// <summary>Streams Windows picker selections directly into workspace staging.</summary>
public sealed class WMWpfPhotoPicker : IWMPhotoPicker
{
    private const string PhotoFilter =
        "照片与 RAW|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.tif;*.tiff;*.dng;*.cr2;*.cr3;*.nef;*.nrw;*.arw;*.sr2;*.raf;*.orf;*.rw2;*.rwl;*.pef;*.3fr;*.iiq;*.srw|普通照片|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.tif;*.tiff|RAW 照片|*.dng;*.cr2;*.cr3;*.nef;*.nrw;*.arw;*.sr2;*.raf;*.orf;*.rw2;*.rwl;*.pef;*.3fr;*.iiq;*.srw";

    public Task<IReadOnlyList<IWMPhotoImportSource>> PickMultipleAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            DefaultExt = ".jpg",
            Multiselect = true,
            Filter = PhotoFilter
        };
        if (dialog.ShowDialog() != true)
            return Task.FromResult<IReadOnlyList<IWMPhotoImportSource>>([]);

        IReadOnlyList<IWMPhotoImportSource> sources = dialog.FileNames
            .Select(path => (IWMPhotoImportSource)new WMPhotoImportSource(
                Path.GetFileName(path),
                token => OpenAsync(path, token),
                MimeType(path)))
            .ToArray();
        return Task.FromResult(sources);
    }

    private static Task<Stream> OpenAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    private static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".tif" or ".tiff" => "image/tiff",
        ".heic" or ".heif" => "image/heic",
        _ => "application/octet-stream"
    };
}
