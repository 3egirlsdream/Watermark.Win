#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed class WMDurableCommandQueue
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task RunAsync(Func<Task> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await command().ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public async Task DrainAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        gate.Release();
    }
}

public sealed class WMTransientPreviewCoordinator
{
    private readonly object gate = new();
    private CancellationTokenSource? debounce;

    public CancellationTokenSource ReplaceDebounce(params CancellationToken[] tokens)
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(tokens);
        CancellationTokenSource? previous;
        lock (gate)
        {
            previous = debounce;
            debounce = next;
        }
        previous?.Cancel();
        previous?.Dispose();
        return next;
    }

    public void Complete(CancellationTokenSource candidate)
    {
        lock (gate)
        {
            if (ReferenceEquals(debounce, candidate)) debounce = null;
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? current;
        lock (gate)
        {
            current = debounce;
            debounce = null;
        }
        current?.Cancel();
        current?.Dispose();
    }
}

public sealed class WMWorkspaceJobCoordinator
{
    private readonly object gate = new();
    private CancellationTokenSource? active;

    public CancellationTokenSource Begin(params CancellationToken[] tokens)
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(tokens);
        CancellationTokenSource? previous;
        lock (gate)
        {
            previous = active;
            active = next;
        }
        previous?.Cancel();
        return next;
    }

    public void Complete(CancellationTokenSource candidate)
    {
        lock (gate)
        {
            if (ReferenceEquals(active, candidate)) active = null;
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? current;
        lock (gate)
        {
            current = active;
            active = null;
        }
        current?.Cancel();
    }
}

public sealed class WMWorkspaceRecoveryService(IWMWorkspaceSessionStore sessionStore)
{
    public Task<WMWorkspaceOpenResult> OpenAsync(string sessionId, CancellationToken cancellationToken) =>
        sessionStore.OpenAsync(sessionId, cancellationToken);

    public Task<WMWorkspaceOpenResult> RecoverAsync(
        string sessionId,
        WMWorkspaceRecoveryAction action,
        IReadOnlyList<string> affectedIds,
        CancellationToken cancellationToken) =>
        sessionStore.RecoverAsync(sessionId, action, affectedIds, cancellationToken);
}

public static class WMWorkspaceProjection
{
    public static IReadOnlyList<WMWorkspaceMedia> Media(WMWorkspaceSession session) =>
        session.Media.Select(media => media with
        {
            Artifact = ResolveArtifact(session, media),
            IsSelected = session.SelectedMediaIds.Contains(media.Id, StringComparer.Ordinal)
        }).ToArray();

    public static IReadOnlyList<WMWorkspaceHistoryItem> History(WMWorkspaceSession session) =>
        session.Transactions.Select((transaction, index) => new WMWorkspaceHistoryItem(
            transaction.Id,
            transaction.Label,
            index + 1,
            index < session.HistoryCursor,
            transaction.CreatedAtUtc)).ToArray();

    public static string RecoveryMessage(WMWorkspaceOpenResult result) =>
        result.Issues.FirstOrDefault()?.Message ?? result.Status switch
        {
            WMWorkspaceOpenStatus.Missing => "编辑会话已过期或不存在。",
            WMWorkspaceOpenStatus.CorruptManifest => "编辑会话清单已损坏。",
            WMWorkspaceOpenStatus.MissingMedia => "编辑会话的素材已被移除。",
            WMWorkspaceOpenStatus.MissingTemplateResource => "模板资源快照不完整。",
            WMWorkspaceOpenStatus.UnsupportedVersion => "请升级应用后再打开该会话。",
            _ => "无法恢复编辑会话。"
        };

    public static WMWorkspaceJobState Job(WMWorkspaceJobCheckpoint? checkpoint) =>
        checkpoint is null
            ? WMWorkspaceJobState.Idle
            : new WMWorkspaceJobState(
                checkpoint.Id,
                checkpoint.Kind,
                checkpoint.Status,
                checkpoint.Status == WMWorkspaceJobStatus.Completed
                    ? WMOperationStage.Completed
                    : WMOperationStage.Queued,
                checkpoint.Status == WMWorkspaceJobStatus.Completed ? 100 : 0,
                checkpoint.Status == WMWorkspaceJobStatus.Interrupted
                    ? "任务已中断，可重新执行"
                    : string.Empty,
                checkpoint.Status is WMWorkspaceJobStatus.Preparing or WMWorkspaceJobStatus.Running,
                checkpoint.Status is WMWorkspaceJobStatus.Interrupted or WMWorkspaceJobStatus.Failed,
                checkpoint.ErrorMessage);

    private static WMImageArtifact ResolveArtifact(WMWorkspaceSession session, WMWorkspaceMedia media)
    {
        if (session.CurrentArtifactIdsByMediaId.TryGetValue(media.Id, out var artifactId))
        {
            var artifact = session.Artifacts.FirstOrDefault(item =>
                string.Equals(item.Id, artifactId, StringComparison.Ordinal));
            if (artifact is not null) return artifact;
        }
        return media.Artifact;
    }
}
