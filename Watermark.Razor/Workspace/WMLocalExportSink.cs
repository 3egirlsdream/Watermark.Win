#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>Desktop fallback used by Mac Catalyst and Windows hosts.</summary>
public sealed class WMLocalExportSink : IWMExportSink
{
    public Task<string> SaveAsync(
        string renderedPath,
        string suggestedFileName,
        WMExportFormat format,
        WMExportDestinationKind destination,
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            renderedPath,
            suggestedFileName,
            format,
            destination,
            destinationDirectory: null,
            cancellationToken: cancellationToken);

    public async Task<string> SaveAsync(
        string renderedPath,
        string suggestedFileName,
        WMExportFormat format,
        WMExportDestinationKind destination,
        string? destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(renderedPath)) throw new FileNotFoundException("导出产物不存在。", renderedPath);
        var directory = ResolveDirectory(destination, destinationDirectory);
        Directory.CreateDirectory(directory);
        var target = UniquePath(directory, suggestedFileName);
        await using var source = new FileStream(
            renderedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous);
        await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return target;
    }

    private static string ResolveDirectory(
        WMExportDestinationKind destination,
        string? destinationDirectory)
    {
        if (destination == WMExportDestinationKind.SystemPicker)
        {
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                throw new InvalidOperationException("请先选择导出目录。");
            return Path.GetFullPath(destinationDirectory);
        }

        return string.IsNullOrWhiteSpace(Global.OutPutPath)
            ? Path.Combine(Global.AppPath.BasePath, "Output")
            : Global.OutPutPath;
    }

    public static string UniquePath(string directory, string suggestedFileName)
    {
        var safeName = Path.GetFileName(suggestedFileName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Litograph.jpg";
        var candidate = Path.Combine(directory, safeName);
        if (!File.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var suffix = 1; suffix < 10_000; suffix++)
        {
            candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("无法为导出文件生成唯一名称。");
    }
}
