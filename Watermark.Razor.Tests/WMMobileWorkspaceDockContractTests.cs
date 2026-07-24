using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMMobileWorkspaceDockContractTests
{
    [Fact]
    public void Dock_IsControlledAndDoesNotOwnRenderingOrBusinessServices()
    {
        var source = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var forbidden = new[]
        {
            "@inject", "WMWorkspaceController", "IWMObjectUrlRegistry",
            "IWMWorkspaceRenderCoordinator", "File.", "Directory.", "CancellationTokenSource"
        };

        Assert.Contains("[Parameter, EditorRequired] public WMWorkspaceState State", source, StringComparison.Ordinal);
        Assert.Contains("ColorPreviewChanged.InvokeAsync", source, StringComparison.Ordinal);
        Assert.Contains("TemplatePreviewChanged.InvokeAsync", source, StringComparison.Ordinal);
        Assert.All(forbidden, token => Assert.DoesNotContain(token, source, StringComparison.Ordinal));
    }

    [Fact]
    public void MobilePresentationProtocol_DeclaresSpaceHostAndStronglyTypedTools()
    {
        var contracts = Read("Watermark.Razor", "Workspace", "WMWorkspaceContracts.cs");
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var page = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor");

        Assert.Contains("public enum WMMobileEditorSpace", contracts, StringComparison.Ordinal);
        Assert.All(new[] { "Small", "Medium", "Large" }, value =>
            Assert.Contains(value, contracts, StringComparison.Ordinal));
        Assert.Contains("public enum WMMobileEditorHostKind", contracts, StringComparison.Ordinal);
        Assert.Contains("StageOverlay", contracts, StringComparison.Ordinal);
        Assert.Contains("public sealed record WMMobileToolPresentation", contracts, StringComparison.Ordinal);
        Assert.Contains("EventCallback<WMMobileToolPresentation> PresentationChanged", dock, StringComparison.Ordinal);
        Assert.Contains("PresentationChanged=\"OnMobilePresentationChangedAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("mobilePresentation.Space", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Dock_UsesExactlyThreeFixedLayersAndNoResizableDrawerState()
    {
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var dockCss = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor.css");
        var page = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor");
        var resize = Read("Watermark.Razor", "wwwroot", "js", "wm-mobile-workspace-dock.js");

        var control = dock.IndexOf("wm-dock-control-region", StringComparison.Ordinal);
        var tools = dock.IndexOf("wm-dock-tools", control, StringComparison.Ordinal);
        var modes = dock.IndexOf("wm-dock-modes", tools, StringComparison.Ordinal);
        Assert.True(control >= 0 && tools > control && modes > tools);
        Assert.Contains("grid-template-rows: minmax(0, 1fr) 78px", dockCss, StringComparison.Ordinal);
        Assert.Contains("calc(52px + var(--workspace-safe-bottom", dockCss, StringComparison.Ordinal);
        Assert.DoesNotContain("wm-dock-handle", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"separator\"", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("OnDockResizeCommitted", page, StringComparison.Ordinal);
        Assert.DoesNotContain("attachDockResize", resize, StringComparison.Ordinal);
        Assert.DoesNotContain("setPointerCapture", resize, StringComparison.Ordinal);
        Assert.DoesNotContain("panel-collapsed", page, StringComparison.Ordinal);
        Assert.DoesNotContain("panel-expanded", page, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileCss_UsesRequestedPortraitAndLandscapeHeightsAndPreservesDesktopInspector()
    {
        var source = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor.css");

        Assert.Contains("min(232px, 30dvh)", source, StringComparison.Ordinal);
        Assert.Contains("min(340px, 44dvh)", source, StringComparison.Ordinal);
        Assert.Contains("min(480px, 62dvh)", source, StringComparison.Ordinal);
        Assert.Contains("196px + var(--workspace-safe-bottom", source, StringComparison.Ordinal);
        Assert.Contains("264px + var(--workspace-safe-bottom", source, StringComparison.Ordinal);
        Assert.Contains("100dvh - var(--toolbar-height) - 12px", source, StringComparison.Ordinal);
        Assert.Contains("padding-bottom: var(--panel-height)", source, StringComparison.Ordinal);
        Assert.Contains(".workspace-desktop-panel { display: none; }", source, StringComparison.Ordinal);
        Assert.Contains(".workspace-desktop-panel { display: grid; }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolMapping_CoversSpecifiedOrderDefaultsAndSpaceLevels()
    {
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");

        Assert.Contains("templateTool = WMMobileEditorTool.TemplatePicker", dock, StringComparison.Ordinal);
        Assert.Contains("colorTool = WMMobileEditorTool.ColorStyle", dock, StringComparison.Ordinal);
        Assert.Contains("multiFrameTool = WMMobileEditorTool.MultiFrameMaterial", dock, StringComparison.Ordinal);
        Assert.Contains("collageTool = WMMobileEditorTool.CollageMaterial", dock, StringComparison.Ordinal);

        AssertOrdered(SliceArray(dock, "TemplateTools", "ColorTools"), "TemplatePicker", "TemplateBorderTop", "TemplateBorderRight", "TemplateBorderBottom", "TemplateBorderLeft", "TemplateScope");
        AssertOrdered(SliceArray(dock, "ColorTools", "MultiFrameTools"), "ColorStyle", "ColorExposure", "ColorContrast", "ColorHighlights", "ColorShadows", "ColorWhites", "ColorBlacks", "ColorTemperature", "ColorTint", "ColorVibrance", "ColorSaturation", "ColorHslHue", "ColorHslSaturation", "ColorHslLuminance", "ColorPresets", "ColorCurve", "ColorReference", "ColorScope");
        AssertOrdered(SliceArray(dock, "MultiFrameTools", "CollageTools"), "MultiFrameMaterial", "MultiFrameMode", "MultiFrameParameters", "MultiFrameGenerate");
        AssertOrdered(SliceArray(dock, "CollageTools", null), "CollageMaterial", "CollageLayout", "CollageGenerate");

        Assert.Contains("ColorPresets, \"预设\", \"grid-nine\", WMMobileEditorSpace.Medium", dock, StringComparison.Ordinal);
        Assert.Contains("ColorCurve, \"曲线\", \"chart-line-up\", WMMobileEditorSpace.Large", dock, StringComparison.Ordinal);
        Assert.Contains("TemplatePicker, \"模板\", \"grid-four\", WMMobileEditorSpace.Medium", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("@SectionButton(\"光影\"", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("@SectionButton(\"颜色\"", dock, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickLooks_UseCompactRowsThatFitTheFixedSmallSpace()
    {
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var dockCss = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor.css");

        Assert.Contains("<div class=\"wm-dock-look-content\">", dock, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: 28px 48px", dockCss, StringComparison.Ordinal);
        Assert.Contains(".wm-dock-look-content .wm-dock-control-card", dockCss, StringComparison.Ordinal);
        Assert.Contains("height: 48px", dockCss, StringComparison.Ordinal);
        Assert.Contains(".wm-dock-look-content ::deep .wm-tool-rail > div", dockCss, StringComparison.Ordinal);
    }

    [Fact]
    public void ModeNavigation_ScrollsHorizontallyWithoutCompressingLabels()
    {
        var dockCss = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor.css");

        Assert.Contains(".wm-dock-modes", dockCss, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto", dockCss, StringComparison.Ordinal);
        Assert.Contains("touch-action: pan-x", dockCss, StringComparison.Ordinal);
        Assert.Contains("scrollbar-width: none", dockCss, StringComparison.Ordinal);
        Assert.Contains("flex: 0 0 78px", dockCss, StringComparison.Ordinal);
        Assert.Contains("min-width: 78px", dockCss, StringComparison.Ordinal);
        Assert.Contains("white-space: nowrap", dockCss, StringComparison.Ordinal);
        Assert.Contains(".wm-dock-modes::-webkit-scrollbar { display: none; }", dockCss, StringComparison.Ordinal);
        Assert.DoesNotContain("grid-template-columns: repeat(5, minmax(0, 1fr))", dockCss, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatePickers_AttachCanvasJsonBeforeRequestingPreview()
    {
        var mobileDock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var templatePanel = Read("Watermark.Razor", "Workspace", "Components", "WMTemplatePanel.razor");
        var controller = Read("Watermark.Razor", "Workspace", "WMWorkspaceController.cs");

        Assert.Contains("canvas is null ? null : Global.CanvasSerialize(canvas)", mobileDock, StringComparison.Ordinal);
        Assert.Contains("canvas is null ? null : Global.CanvasSerialize(canvas)", templatePanel, StringComparison.Ordinal);
        Assert.Contains("ResolveTemplatePreviewEditAsync(edit, cancellationToken)", controller, StringComparison.Ordinal);
        Assert.Contains("所选模板已不存在。", controller, StringComparison.Ordinal);
        Assert.Contains("会话模板快照已丢失", Read("Watermark.Razor", "Workspace", "WMRenderPlan.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void DraftLock_DisablesCrossCategoryHistoryExportAndCompareUntilApplyOrCancel()
    {
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var page = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor");

        Assert.Contains("HasCategoryDraft", dock, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@ModeSwitchDisabled(mode)\"", dock, StringComparison.Ordinal);
        Assert.Contains("Icon=\"lock\"", dock, StringComparison.Ordinal);
        Assert.Contains("workspace-cancel-button", page, StringComparison.Ordinal);
        Assert.Contains("CancelMobileEditAsync", page, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(!State.CanUndo || HasActiveMobileDraft)\"", page, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@HasActiveMobileDraft\"", page, StringComparison.Ordinal);
        Assert.Contains("Controller.DiscardTransientEditsAsync()", page, StringComparison.Ordinal);
        Assert.Contains("Controller.CommitTemplateAsync", page, StringComparison.Ordinal);
        Assert.Contains("Controller.CommitColorDraftAsync", page, StringComparison.Ordinal);
        Assert.Contains("Controller.CommitMultiFrameDraftAsync", page, StringComparison.Ordinal);
        Assert.Contains("Controller.CommitCollageDraftAsync", page, StringComparison.Ordinal);

        var systemBack = page.IndexOf("private bool HandleSystemBack()", StringComparison.Ordinal);
        var discard = page.IndexOf("await CancelMobileEditAsync()", systemBack, StringComparison.Ordinal);
        var exit = page.IndexOf("await BackAsync()", systemBack, StringComparison.Ordinal);
        Assert.True(systemBack >= 0 && discard > systemBack && exit > discard);
    }

    [Fact]
    public void MultiFrameAndCollage_ApplyPersistConfigurationWithoutGenerating()
    {
        var contracts = Read("Watermark.Razor", "Workspace", "WMWorkspaceContracts.cs");
        var controller = Read("Watermark.Razor", "Workspace", "WMWorkspaceController.cs");

        Assert.Contains("WMMultiFrameDraft? MultiFrameConfiguration", contracts, StringComparison.Ordinal);
        Assert.Contains("WMCollageDraft? CollageConfiguration", contracts, StringComparison.Ordinal);
        Assert.Contains("JsonIgnoreCondition.WhenWritingNull", contracts, StringComparison.Ordinal);
        Assert.Contains("TransientEditMode", contracts, StringComparison.Ordinal);

        var multiCommit = SliceMethod(controller, "public Task CommitMultiFrameDraftAsync", "public Task UpdateCollageDraftAsync");
        var collageCommit = SliceMethod(controller, "public Task CommitCollageDraftAsync", "public Task<string> ExecuteCollageAsync");
        Assert.Contains("MultiFrameConfiguration = draft", multiCommit, StringComparison.Ordinal);
        Assert.Contains("CollageConfiguration = draft", collageCommit, StringComparison.Ordinal);
        Assert.Contains("PersistAsync", multiCommit, StringComparison.Ordinal);
        Assert.Contains("PersistAsync", collageCommit, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteMultiFrame", multiCommit, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteCollage", collageCommit, StringComparison.Ordinal);
        Assert.DoesNotContain("QueuePreview", multiCommit, StringComparison.Ordinal);
        Assert.DoesNotContain("QueuePreview", collageCommit, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterCurve_UsesPresetNodesConstraintsAndThirtyTwoMillisecondLatestWinsUpdates()
    {
        var curve = Read("Watermark.Razor", "Workspace", "Components", "WMMobileMasterCurve.razor");
        var curveJs = Read("Watermark.Razor", "Workspace", "Components", "WMMobileMasterCurve.razor.js");
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");

        Assert.Contains("WMMobileMasterCurve", dock, StringComparison.Ordinal);
        Assert.Contains("State.ColorGradeTool.Draft.Grade.MasterCurve", dock, StringComparison.Ordinal);
        Assert.All(new[] { "线性", "增强", "柔和" }, label => Assert.Contains(label, curve, StringComparison.Ordinal));
        Assert.Contains("workingPoints[index - 1].X + .02f", curve, StringComparison.Ordinal);
        Assert.Contains("workingPoints[index + 1].X - .02f", curve, StringComparison.Ordinal);
        Assert.Contains("0 => 0", curve, StringComparison.Ordinal);
        Assert.Contains("_ when index == workingPoints.Count - 1 => 1", curve, StringComparison.Ordinal);
        Assert.Contains("end.X - prior.X) / 6", curve, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", curve, StringComparison.Ordinal);
        Assert.DoesNotContain("(current.X - previous.X) * .4", curve, StringComparison.Ordinal);
        Assert.Contains("UPDATE_INTERVAL_MS = 32", curveJs, StringComparison.Ordinal);
        Assert.Contains("pending = value", curveJs, StringComparison.Ordinal);
        Assert.Contains("flush(true)", curveJs, StringComparison.Ordinal);
        Assert.DoesNotContain("histogram", curve, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Channel", curve, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveTool_AutoScrollsWithoutRestoringResizeSynchronization()
    {
        var page = Read("Watermark.Razor", "BlazorPages", "Mobile", "MobileWorkspace.razor");
        var dock = Read("Watermark.Razor", "Workspace", "Components", "WMMobileWorkspaceDock.razor");
        var script = Read("Watermark.Razor", "wwwroot", "js", "wm-mobile-workspace-dock.js");

        Assert.Contains("data-mobile-tool", dock, StringComparison.Ordinal);
        Assert.Contains("pendingToolScroll", page, StringComparison.Ordinal);
        Assert.Contains("scrollActiveToolIntoView", page, StringComparison.Ordinal);
        Assert.Contains("scrollIntoView", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--panel-height", script, StringComparison.Ordinal);
        Assert.DoesNotContain("invokeMethodAsync", script, StringComparison.Ordinal);
    }

    private static void AssertOrdered(string source, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var current = source.IndexOf(value, previous + 1, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected {value} after index {previous}.");
            previous = current;
        }
    }

    private static string SliceMethod(string source, string startToken, string endToken)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string SliceArray(string source, string name, string? nextName)
    {
        var startToken = $"private static readonly MobileToolSpec[] {name}";
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        var end = nextName is null
            ? source.Length
            : source.IndexOf(
                $"private static readonly MobileToolSpec[] {nextName}",
                start + startToken.Length,
                StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
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
