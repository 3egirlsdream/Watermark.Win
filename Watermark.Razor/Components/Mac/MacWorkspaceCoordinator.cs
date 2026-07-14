#nullable enable
using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac;

/// <summary>
/// Owns the Mac workspace activity state and the latest-wins preview lifetime.
/// UI components consume snapshots and send commands instead of independently
/// maintaining processing flags.
/// </summary>
public sealed class MacWorkspaceCoordinator : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource? previewCancellation;
    private MacWorkspaceState state = new();
    private bool disposed;

    public event Action<MacWorkspaceState> Changed = delegate { };

    public MacWorkspaceState State
    {
        get { lock (gate) return state; }
    }

    public void SetMode(MacWorkspaceMode mode)
    {
        CancelPreview();
        Update(current => current with
        {
            Mode = mode,
            Activity = MacWorkspaceActivity.Idle,
            Stage = WMOperationStage.Queued,
            Message = string.Empty,
            Progress = 0,
            CanCancel = false,
            IsIndeterminate = false,
            ErrorMessage = null
        });
    }

    public CancellationToken BeginPreview(string message)
    {
        CancellationToken token;
        lock (gate)
        {
            ThrowIfDisposed();
            previewCancellation?.Cancel();
            previewCancellation?.Dispose();
            previewCancellation = new CancellationTokenSource();
            token = previewCancellation.Token;
        }
        Update(current => current with
        {
            Activity = MacWorkspaceActivity.Previewing,
            Stage = WMOperationStage.Processing,
            Message = message,
            Progress = 0,
            CanCancel = true,
            IsIndeterminate = true,
            ErrorMessage = null
        });
        return token;
    }

    public void BeginProcessing(string message, WMOperationStage stage = WMOperationStage.Queued) => Update(current => current with
    {
        Activity = MacWorkspaceActivity.Processing,
        Stage = stage,
        Message = message,
        Progress = 0,
        CanCancel = true,
        IsIndeterminate = stage is WMOperationStage.Synchronizing or WMOperationStage.Processing,
        ErrorMessage = null
    });

    public void Report(WMOperationProgress progress) => Update(current => current with
    {
        Activity = current.Activity == MacWorkspaceActivity.Previewing
            ? MacWorkspaceActivity.Previewing
            : MacWorkspaceActivity.Processing,
        Stage = progress.Stage,
        Message = progress.Message,
        Progress = progress.Percentage,
        CanCancel = progress.CanCancel,
        IsIndeterminate = progress.IsIndeterminate
    });

    public void PreviewReady(string message = "预览已更新") => Update(current => current with
    {
        Activity = MacWorkspaceActivity.PreviewReady,
        Stage = WMOperationStage.Completed,
        Message = message,
        Progress = 100,
        CanCancel = true,
        IsIndeterminate = false
    });

    public void Complete(string message) => Update(current => current with
    {
        Activity = MacWorkspaceActivity.Completed,
        Stage = WMOperationStage.Completed,
        Message = message,
        Progress = 100,
        CanCancel = false,
        IsIndeterminate = false,
        ErrorMessage = null
    });

    public void Fail(Exception exception) => Update(current => current with
    {
        Activity = MacWorkspaceActivity.Failed,
        Message = exception.Message,
        CanCancel = false,
        IsIndeterminate = false,
        ErrorMessage = exception.Message
    });

    public void Reset() => Update(current => current with
    {
        Activity = MacWorkspaceActivity.Idle,
        Stage = WMOperationStage.Queued,
        Message = string.Empty,
        Progress = 0,
        CanCancel = false,
        IsIndeterminate = false,
        ErrorMessage = null
    });

    public void CancelPreview()
    {
        lock (gate)
        {
            previewCancellation?.Cancel();
            previewCancellation?.Dispose();
            previewCancellation = null;
        }
    }

    private void Update(Func<MacWorkspaceState, MacWorkspaceState> update)
    {
        MacWorkspaceState next;
        lock (gate)
        {
            ThrowIfDisposed();
            state = next = update(state);
        }
        Changed.Invoke(next);
    }

    public void Dispose()
    {
        if (disposed) return;
        CancelPreview();
        disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
