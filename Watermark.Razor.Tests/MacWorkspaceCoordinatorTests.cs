using Watermark.Razor.Components.Mac;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacWorkspaceCoordinatorTests
{
    [Fact]
    public void PreviewLifecycle_UsesExplicitStates()
    {
        using var coordinator = new MacWorkspaceCoordinator();

        var token = coordinator.BeginPreview("正在预览");
        Assert.False(token.IsCancellationRequested);
        Assert.Equal(MacWorkspaceActivity.Previewing, coordinator.State.Activity);

        coordinator.PreviewReady();
        Assert.Equal(MacWorkspaceActivity.PreviewReady, coordinator.State.Activity);
        Assert.Equal(100, coordinator.State.Progress);

        coordinator.BeginProcessing("正在应用");
        coordinator.Report(new WMOperationProgress(1, 2, "处理中", WMOperationStage.Encoding));
        Assert.Equal(MacWorkspaceActivity.Processing, coordinator.State.Activity);
        Assert.Equal(WMOperationStage.Encoding, coordinator.State.Stage);
        Assert.Equal(50, coordinator.State.Progress);

        coordinator.Complete("完成");
        Assert.Equal(MacWorkspaceActivity.Completed, coordinator.State.Activity);
        Assert.False(coordinator.State.CanCancel);
    }

    [Fact]
    public void StartingNewPreview_CancelsPreviousPreview()
    {
        using var coordinator = new MacWorkspaceCoordinator();
        var first = coordinator.BeginPreview("第一次");

        var second = coordinator.BeginPreview("第二次");

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void SetMode_CancelsPreviewAndResetsTransientState()
    {
        using var coordinator = new MacWorkspaceCoordinator();
        var preview = coordinator.BeginPreview("预览");

        coordinator.SetMode(MacWorkspaceMode.StarTrail);

        Assert.True(preview.IsCancellationRequested);
        Assert.Equal(MacWorkspaceMode.StarTrail, coordinator.State.Mode);
        Assert.Equal(MacWorkspaceActivity.Idle, coordinator.State.Activity);
    }

    [Fact]
    public void SynchronizingAndUnknownPixelProgress_AreIndeterminate()
    {
        using var coordinator = new MacWorkspaceCoordinator();

        coordinator.BeginProcessing("正在同步", WMOperationStage.Synchronizing);
        Assert.Equal(WMOperationStage.Synchronizing, coordinator.State.Stage);

        coordinator.Report(new WMOperationProgress(0, 1, "正在处理", WMOperationStage.Processing));
        Assert.True(coordinator.State.IsIndeterminate);

        coordinator.Report(new WMOperationProgress(0, 1, "正在处理", WMOperationStage.Processing, ItemPercentage: 40));
        Assert.False(coordinator.State.IsIndeterminate);
        Assert.Equal(40, coordinator.State.Progress);
    }
}
