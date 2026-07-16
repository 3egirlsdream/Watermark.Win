using Watermark.Razor.Workspace;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWorkspaceTraceStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "watermark-trace-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Record_StripsPathsAndKeepsOnlyDiagnosticTokens()
    {
        var store = new WMWorkspaceTraceStore(root);
        await store.RecordAsync(new WMWorkspaceTraceEvent(
            DateTime.UtcNow,
            "/Users/person/private-session",
            "/private/photo.jpg?secret=1",
            "job/../../account",
            "preview published /Users/person/photo.jpg",
            new Dictionary<WMWorkspaceMetricStage, int> { [WMWorkspaceMetricStage.Decode] = 1 },
            new Dictionary<WMWorkspaceMetricStage, double> { [WMWorkspaceMetricStage.Decode] = 12.5 },
            1024,
            false,
            false,
            "/private/error"));

        var restored = Assert.Single(await store.ReadLatestAsync());

        Assert.DoesNotContain('/', restored.SessionKey);
        Assert.DoesNotContain('/', restored.Fingerprint!);
        Assert.DoesNotContain('/', restored.JobId!);
        Assert.DoesNotContain('/', restored.EventName);
        Assert.DoesNotContain('/', restored.ErrorCode!);
        Assert.Equal(1, restored.Calls[WMWorkspaceMetricStage.Decode]);
    }

    [Fact]
    public async Task CreateReport_ProducesLocalJsonWithoutPhotoPaths()
    {
        var store = new WMWorkspaceTraceStore(root);
        await store.RecordAsync(new WMWorkspaceTraceEvent(
            DateTime.UtcNow,
            WMWorkspaceTraceStore.SessionKey("session-a"),
            "ABC123",
            null,
            "preview-published",
            new Dictionary<WMWorkspaceMetricStage, int>(),
            new Dictionary<WMWorkspaceMetricStage, double>(),
            2048,
            true,
            false));

        var report = await store.CreateReportAsync();

        Assert.True(File.Exists(report));
        var content = await File.ReadAllTextAsync(report);
        Assert.Contains("preview-published", content);
        Assert.DoesNotContain("session-a", content);
    }

    [Fact]
    public async Task ApplicationLog_IsSanitizedAndIncludedInVersionTwoReport()
    {
        var store = new WMWorkspaceTraceStore(root);
        await store.RecordLogAsync(new WMDiagnosticLogEvent(
            DateTime.UtcNow,
            WMDiagnosticLogLevel.Error,
            "Workspace/Controller",
            "preview failed",
            "Cannot read /Users/person/Pictures/private.jpg token=super-secret user@example.com",
            "System.IO.IOException",
            "0x80131620",
            "/private/session",
            new Dictionary<string, string>
            {
                ["source/path"] = @"C:\Users\person\Pictures\private.jpg",
                ["revision"] = "42"
            },
            "at Preview in /Users/person/Workspace/Controller.cs:line 42"));

        var restored = Assert.Single(await store.ReadLatestLogsAsync());
        Assert.Equal("WorkspaceController", restored.Category);
        Assert.Equal("previewfailed", restored.EventName);
        Assert.Contains("[path]", restored.Message);
        Assert.Contains("[account]", restored.Message);
        Assert.Contains("[redacted]", restored.Message);
        Assert.DoesNotContain("person", restored.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", restored.Message!, StringComparison.Ordinal);
        Assert.DoesNotContain("private/session", restored.SessionKey!);
        Assert.Contains("[path]", restored.StackTrace);
        Assert.DoesNotContain("person", restored.StackTrace!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("42", restored.Properties!["revision"]);

        var imaging = new WMImagingDiagnosticSnapshot(
            "Android 14", "Arm64", 2, true, "native-test", 3,
            1024, 2048, [], DateTime.UtcNow);
        var report = await store.CreateReportAsync(imaging);
        var content = await File.ReadAllTextAsync(report);

        Assert.Contains("\"schemaVersion\": 2", content);
        Assert.Contains("native-test", content);
        Assert.Contains("previewfailed", content);
        Assert.DoesNotContain("super-secret", content);
        Assert.DoesNotContain("user@example.com", content);
        Assert.DoesNotContain("private.jpg", content);
    }

    [Fact]
    public async Task Clear_RemovesTraceLogsAndGeneratedReports()
    {
        var store = new WMWorkspaceTraceStore(root);
        await store.RecordAsync(new WMWorkspaceTraceEvent(
            DateTime.UtcNow, "SESSION", null, null, "preview-published",
            new Dictionary<WMWorkspaceMetricStage, int>(),
            new Dictionary<WMWorkspaceMetricStage, double>(),
            0, false, false));
        await store.RecordLogAsync(new WMDiagnosticLogEvent(
            DateTime.UtcNow, WMDiagnosticLogLevel.Warning,
            "Workspace.Controller", "warning"));
        var report = await store.CreateReportAsync();
        Assert.True(File.Exists(report));

        await store.ClearAsync();

        Assert.Empty(await store.ReadLatestAsync());
        Assert.Empty(await store.ReadLatestLogsAsync());
        Assert.False(File.Exists(report));
    }

    [Fact]
    public async Task LocalExporter_CopiesReportToUserVisibleDiagnosticsDirectory()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.json");
        await File.WriteAllTextAsync(source, "{\"ok\":true}");
        var output = Path.Combine(root, "output");
        var exporter = new WMLocalDiagnosticReportExporter(output);

        var result = await exporter.ExportAsync(source, "diagnostics.json");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Location);
        Assert.Equal("{\"ok\":true}", await File.ReadAllTextAsync(result.Location!));
        Assert.Equal("Diagnostics", new DirectoryInfo(Path.GetDirectoryName(result.Location!)!).Name);
    }

    [Fact]
    public async Task LoggerProvider_CapturesErrorsButFiltersFrameworkInformationNoise()
    {
        var store = new WMWorkspaceTraceStore(root);
        using var provider = new WMDiagnosticLoggerProvider(store);
        var framework = provider.CreateLogger("Microsoft.AspNetCore.Components.Renderer");
        var application = provider.CreateLogger("Watermark.Razor.Workspace.Controller");

        framework.LogInformation("routine render");
        framework.LogError(new InvalidOperationException("render failed"), "component failed");
        application.LogInformation("workspace opened");

        var logs = await store.ReadLatestLogsAsync();
        Assert.Equal(2, logs.Count);
        Assert.DoesNotContain(logs, item => item.Message == "routine render");
        Assert.Contains(logs, item => item.Message == "component failed"
                                      && item.ExceptionType == typeof(InvalidOperationException).FullName);
        Assert.Contains(logs, item => item.Message == "workspace opened");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }
}
