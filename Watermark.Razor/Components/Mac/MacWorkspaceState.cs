#nullable enable
using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac;

public enum MacWorkspaceActivity
{
    Idle,
    Previewing,
    PreviewReady,
    Processing,
    Completed,
    Failed
}

public sealed record MacWorkspaceState
{
    public MacWorkspaceMode Mode { get; init; } = MacWorkspaceMode.Template;
    public MacWorkspaceActivity Activity { get; init; } = MacWorkspaceActivity.Idle;
    public WMOperationStage Stage { get; init; } = WMOperationStage.Queued;
    public string Message { get; init; } = string.Empty;
    public double Progress { get; init; }
    public bool CanCancel { get; init; }
    public bool IsIndeterminate { get; init; }
    public string? ErrorMessage { get; init; }
}
