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

        Assert.All(new[] { "模板", "调色", "多帧", "拼图", "风格", "光影", "颜色", "素材", "布局" },
            label => Assert.Contains(label, source, StringComparison.Ordinal));
        Assert.DoesNotContain("@SectionButton(\"高级\"", source, StringComparison.Ordinal);
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
        Assert.Contains("workspace-apply-button", page, StringComparison.Ordinal);
        Assert.Contains("--toolbar-height: calc(52px + var(--workspace-safe-top))", pageCss, StringComparison.Ordinal);
        Assert.Contains("mdi-export-variant", page, StringComparison.Ordinal);
        Assert.Contains("OnDockResizeCommitted", page, StringComparison.Ordinal);
        Assert.Contains("wm-mobile-workspace-dock.js", page, StringComparison.Ordinal);
        Assert.DoesNotContain("wm-dock-mode-icon", dock, StringComparison.Ordinal);
        Assert.Contains("role=\"separator\"", dock, StringComparison.Ordinal);
        Assert.Contains("touch-action: none", dockCss, StringComparison.Ordinal);
        Assert.Contains("position: fixed", pageCss, StringComparison.Ordinal);
        Assert.Contains("clamp(248px, 32vh, 340px)", pageCss, StringComparison.Ordinal);
        Assert.Contains("@supports (height: 100dvh)", pageCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: minmax(0, 1fr) calc(52px + var(--workspace-safe-bottom", dockCss, StringComparison.Ordinal);
        Assert.Contains("grid-row: 2", dockCss, StringComparison.Ordinal);
        Assert.Contains("background: var(--wm-control-accent)", dockCss, StringComparison.Ordinal);
        Assert.Contains("--wm-control-accent: #ffc200", dockCss, StringComparison.Ordinal);
        Assert.DoesNotContain("@supports (-webkit-touch-callout: none)", pageCss, StringComparison.Ordinal);
        Assert.DoesNotContain("button.is-active { background: #eaf2ff", dockCss, StringComparison.Ordinal);
        Assert.Contains("setPointerCapture", resize, StringComparison.Ordinal);
        Assert.Contains("OnDockResizeCommitted", resize, StringComparison.Ordinal);
        Assert.All(new[] { "Collapsed", "Half", "Expanded" },
            size => Assert.Contains(size, resize, StringComparison.Ordinal));
        Assert.Contains("is-panel-dragging", pageCss, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileDock_ChangesHeightOnlyThroughTheDragCommitPath()
    {
        var page = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor");
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var dockCss = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor.css");
        var resize = Read("Watermark.Razor", "wwwroot", "js", "wm-mobile-workspace-dock.js");

        Assert.DoesNotContain("@onclick=\"CycleRequested\"", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("CycleRequested", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpandedRequested", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("CycleRequested=", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpandedRequested=", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"拖动调整工具坞高度\"", dock, StringComparison.Ordinal);
        Assert.Contains("height: 24px", dockCss, StringComparison.Ordinal);
        Assert.Contains("OnDockResizeCommitted", page, StringComparison.Ordinal);
        Assert.Contains("invokeMethodAsync(\"OnDockResizeCommitted\"", resize, StringComparison.Ordinal);
        Assert.Contains("safeBottomProbe", resize, StringComparison.Ordinal);
        Assert.Contains("52 + safeBottom", resize, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileDock_KeepsCompactModesVisibleAndRequiresExplicitConfirmation()
    {
        var page = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor");
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var dockCss = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor.css");

        Assert.Contains("private bool isEditing;", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("@if (!isEditing)", dock, StringComparison.Ordinal);
        Assert.Contains("<nav class=\"wm-dock-modes\"", dock, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@ModeSwitchDisabled", dock, StringComparison.Ordinal);
        Assert.Contains("if (State.Mode == mode || ModeSwitchDisabled(mode)) return;", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("wm-dock-confirm-edit", dock, StringComparison.Ordinal);
        Assert.Contains("workspace-apply-button", page, StringComparison.Ordinal);
        Assert.Contains("EditingChanged=\"OnMobileEditingChanged\"", page, StringComparison.Ordinal);
        Assert.Contains("await mobileDock.ConfirmCurrentEditAsync()", page, StringComparison.Ordinal);
        Assert.Contains("public Task ConfirmCurrentEditAsync()", dock, StringComparison.Ordinal);
        Assert.Contains("wm-dock-active-row", dock, StringComparison.Ordinal);
        Assert.Contains("wm-dock-configuration", dock, StringComparison.Ordinal);
        Assert.Contains("activeAdjustmentVisible", dock, StringComparison.Ordinal);
        Assert.Contains("mdi-check", page, StringComparison.Ordinal);
        Assert.Contains("EditConfirmed=\"ConfirmMobileEditAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("Controller.CommitTemplateAsync(edit, State.ApplyScope)", page, StringComparison.Ordinal);
        Assert.Contains("Controller.CommitColorDraftAsync(State.ApplyScope)", page, StringComparison.Ordinal);
        Assert.Contains("Controller.DiscardTransientEditsAsync()", page, StringComparison.Ordinal);
        Assert.Contains("string.Equals(State.TemplateEdit?.CanvasJson, edit.CanvasJson", page, StringComparison.Ordinal);
        Assert.Contains("Controller.ImportColorReferenceDraftAsync(source)", page, StringComparison.Ordinal);
        Assert.Contains("Controller.ClearColorReferenceDraftAsync()", page, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginCurrentModeEdit", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("@onfocusin=", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("@onpointerdown=", dock, StringComparison.Ordinal);
        Assert.DoesNotContain(".wm-mobile-dock.is-editing {", dockCss, StringComparison.Ordinal);
        Assert.Contains(".wm-dock-configuration", dockCss, StringComparison.Ordinal);
        Assert.Contains("align-self: end", dockCss, StringComparison.Ordinal);

        var activeRow = dock.IndexOf("wm-dock-active-row", StringComparison.Ordinal);
        var subcategories = dock.IndexOf("@SubcategoryRail", activeRow, StringComparison.Ordinal);
        var content = dock.IndexOf("@StandardContent", subcategories, StringComparison.Ordinal);
        var modes = dock.IndexOf("<nav class=\"wm-dock-modes\"", content, StringComparison.Ordinal);
        Assert.True(activeRow >= 0 && subcategories > activeRow && content > subcategories && modes > content);

        var confirmMethod = dock.IndexOf("private async Task ConfirmEditAsync()", StringComparison.Ordinal);
        var commit = dock.IndexOf("await EditConfirmed.InvokeAsync(mode)", confirmMethod, StringComparison.Ordinal);
        var unlock = dock.IndexOf("SetEditing(false)", confirmMethod, StringComparison.Ordinal);
        Assert.True(confirmMethod >= 0 && commit > confirmMethod && unlock > commit);
    }

    [Fact]
    public void MobileCompactEditors_HideCaptionsAndUseTheNewSliderChrome()
    {
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var dockCss = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor.css");
        var control = Read("Watermark.Razor", "Workspace", "Components", "WMColorParameterControl.razor");
        var borderControl = Read("Watermark.Razor", "Workspace", "Components", "WMTemplateBorderControl.razor");
        var colorPanel = Read("Watermark.Razor", "Workspace", "Components", "WMColorGradePanel.razor");

        Assert.Equal(3, CountOccurrences(dock, "ShowHeader=\"false\""));
        Assert.Contains("[Parameter] public bool ShowHeader { get; set; } = true", control, StringComparison.Ordinal);
        Assert.Contains("@if (ShowHeader)", control, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public bool ShowHeader { get; set; } = true", borderControl, StringComparison.Ordinal);
        Assert.Contains("@if (ShowHeader)", borderControl, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowHeader=\"false\"", colorPanel, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Label\"", control, StringComparison.Ordinal);
        Assert.Contains("aria-valuetext=\"@DisplayValue\"", control, StringComparison.Ordinal);
        Assert.Contains("::-webkit-slider-runnable-track", dockCss, StringComparison.Ordinal);
        Assert.Contains("::-webkit-slider-thumb", dockCss, StringComparison.Ordinal);
        Assert.Contains("::-moz-range-track", dockCss, StringComparison.Ordinal);
        Assert.Contains("height: 14px", dockCss, StringComparison.Ordinal);
        Assert.Contains("#202020", dockCss, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileColorEditor_DistributesAdvancedToolsIntoTheirRelatedCategories()
    {
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");

        Assert.DoesNotContain("<WMColorGradePanel", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("@SectionButton(\"高级\"", dock, StringComparison.Ordinal);
        Assert.Contains("HslKeys", dock, StringComparison.Ordinal);
        Assert.Contains("ChangeHslAsync", dock, StringComparison.Ordinal);
        Assert.Contains("HSL 色彩范围", dock, StringComparison.Ordinal);
        Assert.Contains("ChangeCurveAsync", dock, StringComparison.Ordinal);
        Assert.Contains("线性曲线", dock, StringComparison.Ordinal);
        Assert.Contains("增强曲线", dock, StringComparison.Ordinal);
        Assert.Contains("柔和曲线", dock, StringComparison.Ordinal);
        Assert.Contains("保存预设", dock, StringComparison.Ordinal);
        Assert.Contains("WMApplyScope.Current", dock, StringComparison.Ordinal);
        Assert.Contains("WMApplyScope.Selected", dock, StringComparison.Ordinal);

        var lightSection = dock.IndexOf("colorSection == ColorSection.Light", StringComparison.Ordinal);
        var curve = dock.IndexOf("ChangeCurveAsync", lightSection, StringComparison.Ordinal);
        var colorSection = dock.IndexOf("AriaLabel=\"颜色参数\"", curve, StringComparison.Ordinal);
        var hsl = dock.IndexOf("HslKeys", colorSection, StringComparison.Ordinal);
        Assert.True(lightSection >= 0 && curve > lightSection && colorSection > curve && hsl > colorSection);
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

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
