#nullable enable

using Microsoft.Maui.ApplicationModel.DataTransfer;
using Watermark.Razor.Workspace;

namespace Watermark.Andorid.Models;

public sealed class WMMauiDiagnosticReportExporter : IWMDiagnosticReportExporter
{
    public async Task<WMDiagnosticExportResult> ExportAsync(
        string reportPath,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(reportPath))
            return new WMDiagnosticExportResult(false, null, "诊断报告不存在。");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(
                new ShareFileRequest
                {
                    Title = "导出轻影诊断日志",
                    File = new ShareFile(reportPath, "application/json")
                }));
            return new WMDiagnosticExportResult(true, Path.GetFileName(suggestedFileName));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new WMDiagnosticExportResult(false, null, ex.Message);
        }
    }
}
