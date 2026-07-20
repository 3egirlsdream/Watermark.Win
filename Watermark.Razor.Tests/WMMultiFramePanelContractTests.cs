using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMMultiFramePanelContractTests
{
    [Fact]
    public void SharedPanel_EmitsCommandsWithoutOwningImagingServices()
    {
        var source = Read("Watermark.Razor", "Workspace", "Components", "WMMultiFramePanel.razor");

        Assert.Contains("RunRequested", source, StringComparison.Ordinal);
        Assert.Contains("RoleChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IWMImageStackEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationTokenSource", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopAndMobile_ShareControllerWithoutSharingVisualPanel()
    {
        var desktop = Read("Watermark.Razor", "BlazorPages", "MainViewOSX.razor");
        var desktopInspector = Read("Watermark.Razor", "Components", "Desktop", "WMDesktopModeInspector.razor");
        var mobile = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor");

        Assert.Contains("<WMDesktopModeInspector", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMMultiFramePanel", desktop, StringComparison.Ordinal);
        Assert.Contains("<WMMultiFramePanel", mobile, StringComparison.Ordinal);
        Assert.Contains("Controller.ExecuteMultiFrameAsync", desktop, StringComparison.Ordinal);
        Assert.Contains("Controller.PreviewMultiFrameAsync", desktop, StringComparison.Ordinal);
        Assert.Contains("Controller.ExecuteMultiFrameAsync", mobile, StringComparison.Ordinal);
        Assert.Contains("EventCallback PreviewMultiFrame", desktopInspector, StringComparison.Ordinal);
        Assert.DoesNotContain("IWMImageStackEngine", desktopInspector, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject", desktopInspector, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopImport_AppendsThroughWorkspaceController()
    {
        var desktop = Read("Watermark.Razor", "BlazorPages", "MainViewOSX.razor");

        Assert.Contains("Controller.ImportMediaAsync(sources)", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("MacImageImportService", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("WMEditing" + "Session", desktop, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Watermark.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Unable to find the Watermark repository root.");
    }
}
