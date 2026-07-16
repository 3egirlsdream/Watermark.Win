#nullable enable

using Watermark.Andorid.Models;
using Watermark.Razor.Workspace;

namespace Watermark.Andorid;

/// <summary>
/// Keeps the platform picker result stream alive until the workspace importer
/// stages it directly into the session. Android no longer creates an
/// intermediate Cache/photo-imports copy.
/// </summary>
public sealed class WMMauiPhotoPicker : IWMPhotoPicker
{
    public async Task<IReadOnlyList<IWMPhotoImportSource>> PickMultipleAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "长按多选照片",
            FileTypes = new FilePickerFileType(MacOS.FileType)
        }).ConfigureAwait(false);
        if (selected is null) return [];
        return selected.Select(file =>
        {
            var captured = file;
            return new WMPhotoImportSource(
                captured.FileName,
                async token =>
                {
                    token.ThrowIfCancellationRequested();
                    return await captured.OpenReadAsync().ConfigureAwait(false);
                });
        }).Cast<IWMPhotoImportSource>().ToArray();
    }
}
