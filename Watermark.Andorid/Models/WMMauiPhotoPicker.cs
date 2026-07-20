#nullable enable

using Watermark.Andorid.Models;
using Watermark.Razor.Workspace;

namespace Watermark.Andorid;

/// <summary>
/// Keeps the platform picker result alive until the workspace importer stages
/// it directly into the session. Mac Catalyst opens the Powerbox-authorized
/// path directly; invoking FileResult.OpenReadAsync later from a worker thread
/// may let a managed exception escape an NSFileCoordinator callback and abort
/// the process.
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
        });
        if (selected is null) return [];

        var files = selected.ToArray();
#if MACCATALYST
        var macSources = new List<IWMPhotoImportSource>(files.Length);
        foreach (var file in files)
            macSources.Add(await CreateMacSourceAsync(file, cancellationToken));
        return macSources;
#else
        return files.Select(file =>
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
#endif
    }

#if MACCATALYST
    private static async Task<IWMPhotoImportSource> CreateMacSourceAsync(
        FileResult file,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(file.FullPath) && File.Exists(file.FullPath))
            return LocalFileSource(file.FileName, file.FullPath);

        // Some document providers do not expose FullPath. Materialize those
        // while still on the picker continuation, where NSFileCoordinator owns
        // the provider access, and delete the temporary file after staging.
        var directory = Path.Combine(FileSystem.CacheDirectory, "photo-imports");
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(file.FileName);
        var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
        try
        {
            await using var input = await file.OpenReadAsync();
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return LocalFileSource(file.FileName, temporaryPath, deleteAfterUse: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static IWMPhotoImportSource LocalFileSource(
        string displayName,
        string path,
        bool deleteAfterUse = false) =>
        new WMPhotoImportSource(
            displayName,
            token =>
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
            },
            disposeAsync: deleteAfterUse
                ? () =>
                {
                    TryDelete(path);
                    return ValueTask.CompletedTask;
                }
                : null);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Cache cleanup is best-effort and must not turn a successful import
            // into an application-level failure.
        }
    }
#endif
}
