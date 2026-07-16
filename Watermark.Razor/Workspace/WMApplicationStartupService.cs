#nullable enable

using Watermark.Shared.Enums;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public interface IWMApplicationStartupService
{
    Task InitializeAfterPrivacyConsentAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Restores the mobile startup work that used to live in the legacy home page.
/// Desktop hosts resolve the same service so the shared privacy gate stays portable,
/// but no platform work is performed outside Android and iOS.
/// </summary>
public sealed class WMApplicationStartupService(
    IWMAccountService accounts,
    IClientInstance client,
    APIHelper api,
    IWMWorkspaceTraceStore? traces = null) : IWMApplicationStartupService
{
    internal const string DefaultTemplateId = "B735DFC73A0B4080B11BBCFD3AE833D6";
    private readonly object initializationGate = new();
    private Task? initializationTask;

    public Task InitializeAfterPrivacyConsentAsync(CancellationToken cancellationToken = default)
    {
        if (!Global.IsMobile) return Task.CompletedTask;

        Task task;
        lock (initializationGate)
        {
            initializationTask ??= InitializeCoreAsync();
            task = initializationTask;
        }

        return cancellationToken.CanBeCanceled
            ? task.WaitAsync(cancellationToken)
            : task;
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            Global.PrimaryKey = client.Key();
        }
        catch (Exception ex)
        {
            await RecordAsync(
                "mobile-device-key-failed",
                WMDiagnosticLogLevel.Warning,
                "移动端设备标识初始化失败。",
                ex).ConfigureAwait(false);
        }

        // Preserve the old ordering: restore the account before downloading the
        // default template so server-side ownership/download records see the user.
        await accounts.RefreshAsync().ConfigureAwait(false);

        var markerPath = Path.Combine(Global.AppPath.BasePath, "sys", BootstrapMarkerName);
        var migrationBootstrapPending = !File.Exists(markerPath);
        var templateTask = EnsureDefaultTemplateAsync(migrationBootstrapPending);
        var logoTask = EnsureDefaultLogosAsync();
        var results = await Task.WhenAll(templateTask, logoTask).ConfigureAwait(false);

        if (results.All(result => result))
        {
            await WriteCompletionMarkerAsync(markerPath).ConfigureAwait(false);
            await RecordAsync(
                "mobile-startup-completed",
                WMDiagnosticLogLevel.Information,
                "移动端自动登录与默认资源初始化完成。").ConfigureAwait(false);
        }
        else
        {
            await RecordAsync(
                "mobile-default-resources-pending",
                WMDiagnosticLogLevel.Warning,
                "默认模板或图标尚未安装完成，将在下次启动时重试。").ConfigureAwait(false);
        }
    }

    private async Task<bool> EnsureDefaultTemplateAsync(bool migrationBootstrapPending)
    {
        var templatesRoot = Global.AppPath.TemplatesFolder;
        var defaultConfig = Path.Combine(templatesRoot, DefaultTemplateId, "config.json");
        var rootMissing = !Directory.Exists(templatesRoot);

        // Before the marker exists, also repair upgrades from the migrated UI in
        // which the root directory may already have been created while the bundled
        // default template was never installed. Afterwards, retain the legacy rule
        // and only bootstrap again if the complete templates root is missing.
        if (!rootMissing && (!migrationBootstrapPending || File.Exists(defaultConfig))) return true;

        try
        {
            await api.Download(DefaultTemplateId, string.Empty).ConfigureAwait(false);
            if (File.Exists(defaultConfig)) return true;

            await RecordAsync(
                "mobile-default-template-failed",
                WMDiagnosticLogLevel.Warning,
                "默认模板下载完成后未找到 config.json。").ConfigureAwait(false);
            return false;
        }
        catch (Exception ex)
        {
            await RecordAsync(
                "mobile-default-template-failed",
                WMDiagnosticLogLevel.Warning,
                "默认模板下载失败。",
                ex).ConfigureAwait(false);
            return false;
        }
    }

    private async Task<bool> EnsureDefaultLogosAsync()
    {
        var logoRoot = Global.AppPath.LogoesFolder;
        var logoState = InspectDirectory(logoRoot);
        if (logoState == ResourceDirectoryState.ContainsFiles) return true;
        if (logoState == ResourceDirectoryState.Unreadable)
        {
            await RecordAsync(
                "mobile-default-logos-unreadable",
                WMDiagnosticLogLevel.Warning,
                "默认图标目录当前无法读取，已保留目录内容。").ConfigureAwait(false);
            return false;
        }

        try
        {
            // APIHelper intentionally skips an existing directory. A previous
            // interrupted first-run download can leave only empty directories, so
            // remove that empty shell to make the next attempt effective.
            if (Directory.Exists(logoRoot)) Directory.Delete(logoRoot, true);
            await api.DownloadLogoes().ConfigureAwait(false);
            if (InspectDirectory(logoRoot) == ResourceDirectoryState.ContainsFiles) return true;

            await RecordAsync(
                "mobile-default-logos-failed",
                WMDiagnosticLogLevel.Warning,
                "默认图标下载完成后未找到图标文件。").ConfigureAwait(false);
            return false;
        }
        catch (Exception ex)
        {
            await RecordAsync(
                "mobile-default-logos-failed",
                WMDiagnosticLogLevel.Warning,
                "默认图标下载失败。",
                ex).ConfigureAwait(false);
            return false;
        }
    }

    private static ResourceDirectoryState InspectDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return ResourceDirectoryState.Missing;
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any()
                ? ResourceDirectoryState.ContainsFiles
                : ResourceDirectoryState.Empty;
        }
        catch
        {
            return ResourceDirectoryState.Unreadable;
        }
    }

    private async Task WriteCompletionMarkerAsync(string markerPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(markerPath)!;
            Directory.CreateDirectory(directory);
            var temporary = $"{markerPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, DateTime.UtcNow.ToString("O")).ConfigureAwait(false);
                File.Move(temporary, markerPath, true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            await RecordAsync(
                "mobile-default-resources-marker-failed",
                WMDiagnosticLogLevel.Warning,
                "默认资源已安装，但完成标记写入失败。",
                ex).ConfigureAwait(false);
        }
    }

    private Task RecordAsync(
        string eventName,
        WMDiagnosticLogLevel level,
        string message,
        Exception? exception = null) =>
        traces?.RecordLogAsync(new WMDiagnosticLogEvent(
            DateTime.UtcNow,
            level,
            "Application.Startup",
            eventName,
            message,
            exception?.GetType().FullName,
            exception is null ? null : $"0x{exception.HResult:X8}",
            StackTrace: exception?.StackTrace)) ?? Task.CompletedTask;

    private static string BootstrapMarkerName => Global.DeviceType == DeviceType.IOS
        ? "ios-default-resources.v1"
        : "android-default-resources.v1";

    private enum ResourceDirectoryState
    {
        Missing,
        Empty,
        ContainsFiles,
        Unreadable
    }
}
