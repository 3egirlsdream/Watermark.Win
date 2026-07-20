#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public interface IWMDesktopStartupService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class WMDesktopStartupService(
    IClientInstance client,
    IWMAccountService accounts,
    IWMMembershipService membership,
    WMTemplateLibraryService templates,
    IWMWorkspaceTraceStore? traces = null) : IWMDesktopStartupService
{
    private readonly object gate = new();
    private Task? initialization;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (gate) task = initialization ??= InitializeCoreAsync();
        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    private async Task InitializeCoreAsync()
    {
        var tasks = new List<Task>();
        try { Global.PrimaryKey = client.Key(); }
        catch (Exception ex) { tasks.Add(RecordAsync("desktop-device-key-failed", ex)); }
        try { await GlobalConfig.InitConfig().ConfigureAwait(false); }
        catch (Exception ex) { tasks.Add(RecordAsync("desktop-config-failed", ex)); }
        tasks.Add(RunBestEffortAsync("desktop-account-failed", () => accounts.RefreshAsync()));
        tasks.Add(RunBestEffortAsync("desktop-membership-failed", () => membership.ReconcilePendingAsync()));
        tasks.Add(RunBestEffortAsync("desktop-templates-failed", () => templates.GetOrRefreshAsync()));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RunBestEffortAsync(string eventName, Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception ex) { await RecordAsync(eventName, ex).ConfigureAwait(false); }
    }

    private Task RecordAsync(string eventName, Exception exception) => traces?.RecordLogAsync(
        new WMDiagnosticLogEvent(
            DateTime.UtcNow,
            WMDiagnosticLogLevel.Warning,
            "Application.DesktopStartup",
            eventName,
            exception.Message,
            exception.GetType().FullName,
            $"0x{exception.HResult:X8}",
            StackTrace: exception.StackTrace)) ?? Task.CompletedTask;
}
