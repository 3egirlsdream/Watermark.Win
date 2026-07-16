#if ANDROID
#nullable enable

using Android.Content;
using Android.OS;
using Android.Provider;
using System.Runtime.Versioning;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;

namespace Watermark.Andorid;

public sealed class WMAndroidExportSink : IWMExportSink
{
    public async Task<string> SaveAsync(
        string renderedPath,
        string suggestedFileName,
        WMExportFormat format,
        WMExportDestinationKind destination,
        CancellationToken cancellationToken = default)
    {
        var activity = MainActivity.Instance
                       ?? throw new InvalidOperationException("Android 页面尚未就绪。");
        if (!File.Exists(renderedPath)) throw new FileNotFoundException("导出产物不存在。", renderedPath);
        var fileName = Path.GetFileName(suggestedFileName);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "Litograph.jpg";
        var mimeType = format switch
        {
            WMExportFormat.Png16 => "image/png",
            WMExportFormat.Tiff16 => "image/tiff",
            _ => "image/jpeg"
        };

        if (format == WMExportFormat.Tiff16 || destination == WMExportDestinationKind.SystemPicker)
            return await SaveWithDocumentPickerAsync(
                activity, renderedPath, fileName, mimeType, cancellationToken).ConfigureAwait(false);

        if (IsAtLeastAndroid29)
            return await SaveToMediaStoreAsync(
                activity, renderedPath, fileName, mimeType, cancellationToken).ConfigureAwait(false);

        var permission = await Permissions.RequestAsync<Permissions.StorageWrite>().ConfigureAwait(false);
        if (permission != PermissionStatus.Granted)
            throw new UnauthorizedAccessException("需要照片存储权限才能保存到系统相册。");

#pragma warning disable CA1422
        var pictures = Android.OS.Environment.GetExternalStoragePublicDirectory(
            Android.OS.Environment.DirectoryPictures)?.AbsolutePath
            ?? throw new IOException("系统图片目录不可用。");
#pragma warning restore CA1422
        var directory = Path.Combine(pictures, "Litograph");
        Directory.CreateDirectory(directory);
        var target = WMLocalExportSink.UniquePath(directory, fileName);
        await CopyAsync(renderedPath, target, cancellationToken).ConfigureAwait(false);
        Android.Media.MediaScannerConnection.ScanFile(
            activity, [target], [mimeType], null);
        return target;
    }

    [SupportedOSPlatform("android29.0")]
    private static async Task<string> SaveToMediaStoreAsync(
        MainActivity activity,
        string renderedPath,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken)
    {
        var resolver = activity.ContentResolver
                       ?? throw new InvalidOperationException("Android ContentResolver 不可用。");
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, mimeType);
        values.Put(MediaStore.IMediaColumns.RelativePath, "Pictures/Litograph");
        values.Put(MediaStore.IMediaColumns.IsPending, 1);
        var collection = MediaStore.Images.Media.ExternalContentUri
                         ?? throw new InvalidOperationException("系统相册不可用。");
        var uri = resolver.Insert(collection, values)
                  ?? throw new IOException("无法创建系统相册文件。");
        try
        {
            await using var source = new FileStream(
                renderedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = resolver.OpenOutputStream(uri)
                                     ?? throw new IOException("无法写入系统相册文件。");
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            var completed = new ContentValues();
            completed.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, completed, null, null);
            return uri.ToString() ?? fileName;
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
    }

    [SupportedOSPlatformGuard("android29.0")]
    private static bool IsAtLeastAndroid29 => Build.VERSION.SdkInt >= BuildVersionCodes.Q;

    private static async Task<string> SaveWithDocumentPickerAsync(
        MainActivity activity,
        string renderedPath,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken)
    {
        var uri = await activity.CreateDocumentAsync(
            fileName,
            mimeType,
            cancellationToken).ConfigureAwait(false);
        if (uri is null) throw new System.OperationCanceledException("未选择 TIFF 保存位置。", cancellationToken);
        var resolver = activity.ContentResolver
                       ?? throw new InvalidOperationException("Android ContentResolver 不可用。");
        await using var source = new FileStream(
            renderedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = resolver.OpenOutputStream(uri, "w")
                                 ?? throw new IOException("无法写入所选 TIFF 文件。");
        await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return uri.ToString() ?? fileName;
    }

    private static async Task CopyAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var target = new FileStream(
            targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
#endif
