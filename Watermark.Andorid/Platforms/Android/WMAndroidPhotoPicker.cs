#nullable enable

using Android.Provider;
using Watermark.Razor.Workspace;

namespace Watermark.Andorid;

/// <summary>
/// Opens Android's photo-first picker instead of DocumentsUI and exposes the
/// granted content streams directly to the session stager.
/// </summary>
public sealed class WMAndroidPhotoPicker : IWMPhotoPicker
{
    public async Task<IReadOnlyList<IWMPhotoImportSource>> PickMultipleAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activity = MainActivity.Instance
            ?? throw new InvalidOperationException("Android 主界面尚未就绪。");
        var uris = await activity.PickImagesAsync(cancellationToken).ConfigureAwait(false);
        if (uris.Count == 0) return [];

        var resolver = activity.ContentResolver
            ?? throw new InvalidOperationException("Android ContentResolver 不可用。");
        return uris.Select((uri, index) =>
        {
            var capturedUri = uri;
            var displayName = ResolveDisplayName(resolver, capturedUri)
                ?? $"photo-{index + 1}.jpg";
            var mimeType = resolver.GetType(capturedUri);
            return (IWMPhotoImportSource)new WMPhotoImportSource(
                displayName,
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    Stream stream = resolver.OpenInputStream(capturedUri)
                        ?? throw new IOException($"无法打开所选照片：{displayName}");
                    return Task.FromResult(stream);
                },
                mimeType);
        }).ToArray();
    }

    private static string? ResolveDisplayName(
        Android.Content.ContentResolver resolver,
        Android.Net.Uri uri)
    {
        try
        {
            using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor is null || !cursor.MoveToFirst()) return null;
            var column = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
            return column >= 0 ? cursor.GetString(column) : null;
        }
        catch
        {
            return null;
        }
    }
}
