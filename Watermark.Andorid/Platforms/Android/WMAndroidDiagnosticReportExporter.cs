#if ANDROID
#nullable enable

using Watermark.Razor.Workspace;

namespace Watermark.Andorid;

public sealed class WMAndroidDiagnosticReportExporter : IWMDiagnosticReportExporter
{
    public async Task<WMDiagnosticExportResult> ExportAsync(
        string reportPath,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var activity = MainActivity.Instance;
        if (activity is null)
            return new WMDiagnosticExportResult(false, null, "Android 页面尚未就绪。");
        if (!File.Exists(reportPath))
            return new WMDiagnosticExportResult(false, null, "诊断报告不存在。");

        try
        {
            var fileName = Path.GetFileName(suggestedFileName);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"litograph-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var uri = await activity.CreateDocumentAsync(
                fileName,
                "application/json",
                cancellationToken).ConfigureAwait(false);
            if (uri is null)
                return new WMDiagnosticExportResult(false, null, "已取消导出。");

            var resolver = activity.ContentResolver;
            if (resolver is null)
                return new WMDiagnosticExportResult(false, null, "Android 文件服务不可用。");
            await using var source = new FileStream(
                reportPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = resolver.OpenOutputStream(uri, "w")
                                     ?? throw new IOException("无法写入所选文件。");
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new WMDiagnosticExportResult(true, uri.ToString());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new WMDiagnosticExportResult(false, null, ex.Message);
        }
    }
}
#endif
