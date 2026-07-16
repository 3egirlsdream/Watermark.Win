#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>Desktop fallback that writes a user-visible report beside normal output.</summary>
public sealed class WMLocalDiagnosticReportExporter : IWMDiagnosticReportExporter
{
    private readonly string? outputRootOverride;

    public WMLocalDiagnosticReportExporter() { }

    public WMLocalDiagnosticReportExporter(string outputRoot) =>
        outputRootOverride = outputRoot;

    public async Task<WMDiagnosticExportResult> ExportAsync(
        string reportPath,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(reportPath))
            return new WMDiagnosticExportResult(false, null, "诊断报告不存在。");

        try
        {
            var outputRoot = outputRootOverride ?? (string.IsNullOrWhiteSpace(Global.OutPutPath)
                ? Path.Combine(Global.AppPath.BasePath, "Output")
                : Global.OutPutPath);
            var directory = Path.Combine(outputRoot, "Diagnostics");
            Directory.CreateDirectory(directory);
            var fileName = Path.GetFileName(suggestedFileName);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"litograph-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var target = WMLocalExportSink.UniquePath(directory, fileName);
            await using var source = new FileStream(
                reportPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(
                target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous);
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new WMDiagnosticExportResult(true, target);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new WMDiagnosticExportResult(false, null, ex.Message);
        }
    }
}
