using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMMobileWorkspaceDockContractTests
{
    [Fact]
    public void Dock_IsControlledAndDoesNotOwnBusinessServices()
    {
        var source = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var forbidden = new[]
        {
            "@inject", "WMWorkspaceController", "IWMObjectUrlRegistry",
            "IWMWorkspaceRenderCoordinator", "File.", "Directory.", "CancellationTokenSource"
        };

        Assert.Contains("[Parameter, EditorRequired] public WMWorkspaceState State", source, StringComparison.Ordinal);
        Assert.All(forbidden, token => Assert.DoesNotContain(token, source, StringComparison.Ordinal));
    }

    [Fact]
    public void Dock_ContainsAllModesAndShallowToolCategories()
    {
        var source = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");

        Assert.All(new[] { "模板", "调色", "多帧", "拼图", "风格", "光影", "颜色", "高级", "素材", "布局" },
            label => Assert.Contains(label, source, StringComparison.Ordinal));
        Assert.Contains("WMToolRail", source, StringComparison.Ordinal);
        Assert.Contains("WMApplyScopeSelector", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileAndDesktop_ReuseTheSameAtomicParameterControls()
    {
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var template = Read("Watermark.Razor", "Workspace", "Components", "WMTemplatePanel.razor");
        var color = Read("Watermark.Razor", "Workspace", "Components", "WMColorGradePanel.razor");

        Assert.Contains("WMColorParameterControl", dock, StringComparison.Ordinal);
        Assert.Contains("WMColorParameterControl", color, StringComparison.Ordinal);
        Assert.Contains("WMTemplateBorderControl", dock, StringComparison.Ordinal);
        Assert.Contains("WMTemplateBorderControl", template, StringComparison.Ordinal);
        Assert.Contains("WMWorkspacePanelMutations.SetColorValue", dock, StringComparison.Ordinal);
        Assert.Contains("WMWorkspacePanelMutations.SetColorValue", color, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileWorkspace_UsesIndependentExportDrawerAndOrderedBackHandling()
    {
        var source = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor");

        Assert.Contains("<WMMobileWorkspaceDock", source, StringComparison.Ordinal);
        Assert.Contains("workspace-export-drawer", source, StringComparison.Ordinal);
        Assert.Contains("OpenExportDrawer", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("else if (showMoreMenu)", StringComparison.Ordinal)
                    < source.IndexOf("else if (showExportDrawer)", StringComparison.Ordinal));
        Assert.True(source.IndexOf("else if (showExportDrawer)", StringComparison.Ordinal)
                    < source.LastIndexOf("mobileDock?.TryCloseTransient()", StringComparison.Ordinal));
    }

    [Fact]
    public void MobileCss_UsesRequestedDockHeightsAndPreservesDesktopInspector()
    {
        var source = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor.css");

        Assert.Contains("clamp(248px, 32dvh, 340px)", source, StringComparison.Ordinal);
        Assert.Contains("min(72dvh", source, StringComparison.Ordinal);
        Assert.Contains("min(52dvh", source, StringComparison.Ordinal);
        Assert.Contains(".workspace-desktop-panel { display: none; }", source, StringComparison.Ordinal);
        Assert.Contains(".workspace-desktop-panel { display: grid; }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileDock_HasTouchResizeStateSyncAndCompactNavigationChrome()
    {
        var page = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor");
        var pageCss = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor.css");
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var dockCss = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor.css");
        var resize = Read("Watermark.Razor", "wwwroot", "js", "wm-mobile-workspace-dock.js");

        Assert.Contains("workspace-toolbar-actions", page, StringComparison.Ordinal);
        Assert.Contains("workspace-history-actions", page, StringComparison.Ordinal);
        Assert.Contains("mdi-export-variant", page, StringComparison.Ordinal);
        Assert.Contains("OnDockResizeCommitted", page, StringComparison.Ordinal);
        Assert.Contains("wm-mobile-workspace-dock.js", page, StringComparison.Ordinal);
        Assert.Contains("wm-dock-mode-icon", dock, StringComparison.Ordinal);
        Assert.Contains("role=\"separator\"", dock, StringComparison.Ordinal);
        Assert.Contains("touch-action: none", dockCss, StringComparison.Ordinal);
        Assert.Contains("position: fixed", pageCss, StringComparison.Ordinal);
        Assert.Contains("clamp(248px, 32vh, 340px)", pageCss, StringComparison.Ordinal);
        Assert.Contains("@supports (height: 100dvh)", pageCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: minmax(0, 1fr) calc(72px + var(--workspace-safe-bottom", dockCss, StringComparison.Ordinal);
        Assert.Contains("grid-row: 2", dockCss, StringComparison.Ordinal);
        Assert.DoesNotContain("@supports (-webkit-touch-callout: none)", pageCss, StringComparison.Ordinal);
        Assert.DoesNotContain("button.is-active { background: #eaf2ff", dockCss, StringComparison.Ordinal);
        Assert.Contains("setPointerCapture", resize, StringComparison.Ordinal);
        Assert.Contains("OnDockResizeCommitted", resize, StringComparison.Ordinal);
        Assert.All(new[] { "Collapsed", "Half", "Expanded" },
            size => Assert.Contains(size, resize, StringComparison.Ordinal));
        Assert.Contains("is-panel-dragging", pageCss, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var root = FindRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Watermark.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Watermark.sln.");
    }
}
