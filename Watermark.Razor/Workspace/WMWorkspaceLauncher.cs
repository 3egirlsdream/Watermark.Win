#nullable enable

namespace Watermark.Razor.Workspace;

/// <summary>
/// Creates a persisted workspace from platform-owned streams. Keeping this
/// orchestration outside pages guarantees that every host stages each source
/// exactly once before navigating to the workspace route.
/// </summary>
public sealed class WMWorkspaceLauncher(
    IWMWorkspaceSessionStore sessionStore,
    IWMWorkspaceTraceStore? traceStore = null) : IWMWorkspaceLauncher
{
    public async Task<string> CreateFromSourcesAsync(
        WMWorkspaceMode mode,
        IReadOnlyList<IWMPhotoImportSource> sources,
        string? templateId,
        CancellationToken token)
    {
        try
        {
            var sessionId = await sessionStore.CreateAsync(mode, sources, templateId, token).ConfigureAwait(false);
            var verification = await sessionStore.OpenAsync(sessionId, token).ConfigureAwait(false);
            if (!verification.IsOpened)
            {
                await RecordLogSafeAsync(
                    WMDiagnosticLogLevel.Error,
                    "workspace-session-verification-failed",
                    $"新建工作台会话落盘校验失败：{verification.Status}。",
                    sessionId,
                    new Dictionary<string, string>
                    {
                        ["status"] = verification.Status.ToString(),
                        ["sourceCount"] = sources.Count.ToString(),
                        ["mode"] = mode.ToString()
                    }).ConfigureAwait(false);
                await sessionStore.DeleteAsync(sessionId).ConfigureAwait(false);
                throw new IOException($"新建工作台会话未能持久化（{verification.Status}），请重新选择照片。");
            }

            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Information,
                "workspace-session-created",
                "工作台会话已创建并通过落盘校验。",
                sessionId,
                new Dictionary<string, string>
                {
                    ["sourceCount"] = sources.Count.ToString(),
                    ["mode"] = mode.ToString()
                }).ConfigureAwait(false);
            return sessionId;
        }
        finally
        {
            foreach (var source in sources)
                await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RecordLogSafeAsync(
        WMDiagnosticLogLevel level,
        string eventName,
        string message,
        string sessionId,
        IReadOnlyDictionary<string, string> properties)
    {
        if (traceStore is null) return;
        try
        {
            await traceStore.RecordLogAsync(new WMDiagnosticLogEvent(
                DateTime.UtcNow,
                level,
                "Workspace.Launcher",
                eventName,
                message,
                SessionKey: WMWorkspaceTraceStore.SessionKey(sessionId),
                Properties: properties)).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must not change session creation behavior.
        }
    }
}
