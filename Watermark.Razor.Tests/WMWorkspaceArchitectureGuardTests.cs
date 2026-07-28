using System.Text.RegularExpressions;
using Watermark.Razor.Components.Compatibility;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWorkspaceArchitectureGuardTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string RazorRoot = Path.Combine(RepositoryRoot, "Watermark.Razor");

    [Fact]
    public void Workspace_HasExactlyOneRouteLifecycle()
    {
        var routeOwners = RazorFiles()
            .Where(path => Regex.IsMatch(File.ReadAllText(path), "@page\\s+\"/workspace/", RegexOptions.CultureInvariant))
            .Select(Relative)
            .ToArray();

        Assert.Equal(["Watermark.Razor/BlazorPages/WMWorkspacePage.razor"], routeOwners);
    }

    [Fact]
    public void RemovedCreationRoutes_AreNotDeclaredOrNavigatedTo()
    {
        var source = string.Join('\n', SourceFiles().Select(File.ReadAllText));

        Assert.DoesNotMatch("@page\\s+\"/(?:preview|design|split)(?:/|\")", source);
        Assert.DoesNotMatch("NavigateTo\\(\"/(?:preview|design|split)(?:/|\")", source);
    }

    [Fact]
    public void ActiveWorkspaceShells_DoNotOwnBusinessInfrastructure()
    {
        var mobile = Read("Watermark.Razor/BlazorPages/Mobile/MobileWorkspace.razor");
        var desktop = Read("Watermark.Razor/BlazorPages/MainViewOSX.razor");
        var combined = mobile + desktop;
        var forbidden = new[]
        {
            "APIHelper", "IClientInstance", "IWMObjectUrlRegistry", "WMEditingSession",
            "MacWorkspaceCoordinator", "MacRenderPlan", "BuildRenderPlan", "Directory.", "File."
        };

        Assert.All(forbidden, token => Assert.DoesNotContain(token, combined, StringComparison.Ordinal));
        Assert.DoesNotContain("MacTemplateDesigner", mobile, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateLibraryPage_UsesOnlyTemplateServicesForPersistenceAndMarket()
    {
        var source = Read("Watermark.Razor/BlazorPages/Mobile/MobileTemplates.razor");
        var forbidden = new[] { "APIHelper", "WMTemplateStore", "Directory.", "File.", "IClientInstance" };

        Assert.All(forbidden, token => Assert.DoesNotContain(token, source, StringComparison.Ordinal));
        Assert.Contains("IWMTemplateMarketplaceService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedMacBusinessWrappers_HaveNoSourceReferences()
    {
        var source = string.Join('\n', SourceFiles().Select(File.ReadAllText));
        var removedTypes = new[]
        {
            "MacEditingSession", "MacWorkspaceCoordinator", "MacRenderPlan", "MacTemplateStore",
            "MacTemplateEditorState", "MacControlTree", "MacFullResolutionRenderService",
            "MacImageImportService", "MacTemplateLibraryService"
        };

        Assert.All(removedTypes, token => Assert.DoesNotContain(token, source, StringComparison.Ordinal));
    }

    [Fact]
    public void MobileWorkspaceNavigation_PushesWorkspaceAndRestoresTemplateOrigin()
    {
        var create = Read("Watermark.Razor/BlazorPages/Mobile/MobileCreate.razor");
        var templates = Read("Watermark.Razor/BlazorPages/Mobile/MobileTemplates.razor");
        var workspace = Read("Watermark.Razor/BlazorPages/Mobile/MobileWorkspace.razor");
        var route = Read("Watermark.Razor/BlazorPages/WMWorkspacePage.razor");

        Assert.Contains("NavigationHistory.NavigateTo($\"/workspace/{id}\");", create, StringComparison.Ordinal);
        Assert.Contains("NavigationHistory.NavigateTo($\"/workspace/{id}\");", templates, StringComparison.Ordinal);
        Assert.Contains("TemplateTabPath(activeTab)", templates, StringComparison.Ordinal);
        Assert.Contains("NavigationHistory.GoBack(returnPath);", workspace, StringComparison.Ordinal);
        Assert.Contains("templateDesigner.CanNavigateAwayAsync()", workspace, StringComparison.Ordinal);
        Assert.Contains("State.Recovery?.Status == WMWorkspaceOpenStatus.Missing", workspace, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/create\", replace: true);", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionId=\"SessionId\"", route, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(route, "SessionId=\\\"@SessionId\\\"").Cast<Match>());
        var desktopRoute = Read("Watermark.Razor/BlazorPages/WMDesktopWorkspacePage.razor");
        Assert.Contains("/mac/workspace/{SessionId}", desktopRoute, StringComparison.Ordinal);
        Assert.Contains("/desktop/workspace/{SessionId}", desktopRoute, StringComparison.Ordinal);
        Assert.Contains("@layout WMWorkspaceRouteLayout", route, StringComparison.Ordinal);
        var routeLayout = Read("Watermark.Razor/Components/Layout/WMWorkspaceRouteLayout.razor");
        Assert.Contains("Global.DeviceType is DeviceType.Mac or DeviceType.Win", routeLayout, StringComparison.Ordinal);
        Assert.Contains("<MobileWorkspaceLayout Body=\"Body\" />", routeLayout, StringComparison.Ordinal);
        var desktopWorkspace = Read("Watermark.Razor/BlazorPages/MainViewOSX.razor");
        Assert.DoesNotContain("/templates?tab=market", desktopWorkspace, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateTo(\"/create\"", desktopWorkspace, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileTemplateDesigner_UsesPageBackCanonicalPreviewAndFixedPanelSpaces()
    {
        var designer = Read("Watermark.Razor/Workspace/Components/WMTemplateDesigner.razor");
        var designerCss = Read("Watermark.Razor/Workspace/Components/WMTemplateDesigner.razor.css");
        var toolRailCss = Read("Watermark.Razor/Workspace/Components/WMPosterMobileToolRail.razor.css");
        var canvasScript = Read("Watermark.Razor/wwwroot/js/mac-template-canvas.js");
        var sliderCss = Read("Watermark.Razor/Components/Mac/MacSlider.razor.css");
        var shell = Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor");
        var workspace = Read("Watermark.Razor/BlazorPages/Mobile/MobileWorkspace.razor");
        var controller = Read("Watermark.Razor/Workspace/WMWorkspaceController.cs");

        Assert.Contains("TemplateLibrary.GetOrRefreshAsync()", designer, StringComparison.Ordinal);
        Assert.Contains("Global.InitFonts([loadedCanvas])", designer, StringComparison.Ordinal);
        Assert.Contains("CanNavigateAwayAsync", designer, StringComparison.Ordinal);
        var backHandler = designer[designer.IndexOf("public async Task HandleBackAsync()", StringComparison.Ordinal)..
            designer.IndexOf("public async Task<bool> CanNavigateAwayAsync()", StringComparison.Ordinal)];
        Assert.Contains("CloseMobileTextFieldLibraryAsync", backHandler, StringComparison.Ordinal);
        Assert.Contains("CancelMobileTextContent", backHandler, StringComparison.Ordinal);
        Assert.Contains("Select(null)", backHandler, StringComparison.Ordinal);
        Assert.Contains("--mobile-designer-panel-height", designerCss, StringComparison.Ordinal);
        Assert.Contains("min(232px, 30dvh)", designerCss, StringComparison.Ordinal);
        Assert.Contains("min(340px, 44dvh)", designerCss, StringComparison.Ordinal);
        Assert.Contains("min(480px, 62dvh)", designerCss, StringComparison.Ordinal);
        Assert.DoesNotContain("mobile-designer-drawer-handle", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"separator\"", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("attachDrawerResize", designer, StringComparison.Ordinal);
        Assert.Contains("<WMPosterMobileToolRail", designer, StringComparison.Ordinal);
        Assert.Contains("ActiveToolId=\"@presentation.ActiveToolId\"", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("Compact=", designer, StringComparison.Ordinal);
        Assert.Contains("mobile-panel-none", designer, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: 52px minmax(0, 1fr) calc(78px + env(safe-area-inset-bottom));", designerCss, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto;", toolRailCss, StringComparison.Ordinal);
        Assert.Contains("flex: 0 0 62px;", toolRailCss, StringComparison.Ordinal);
        Assert.Contains("touch-action: pan-x;", toolRailCss, StringComparison.Ordinal);
        Assert.Contains("mobile-space-large:not(.mobile-properties-panel) ::deep .designer-toolbar", designerCss, StringComparison.Ordinal);
        Assert.Contains("mobile-space-large.mobile-properties-panel .designer-workspace", designerCss, StringComparison.Ordinal);
        Assert.Contains(".wm-template-designer ::deep *", designerCss, StringComparison.Ordinal);
        Assert.Contains("user-select: none", designerCss, StringComparison.Ordinal);
        Assert.Contains("::deep input", designerCss, StringComparison.Ordinal);
        Assert.Contains("user-select: text", designerCss, StringComparison.Ordinal);
        Assert.Contains("touch-action: pan-y", sliderCss, StringComparison.Ordinal);
        Assert.Contains("<ExifConfig", designer, StringComparison.Ordinal);
        Assert.Contains("<WMPosterAssetDrawer", designer, StringComparison.Ordinal);
        Assert.Contains("<WMPosterAssetCropPanel", designer, StringComparison.Ordinal);
        Assert.Contains("<WMCropCanvas", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("WMPosterAssetCropPreview", designer, StringComparison.Ordinal);
        Assert.Contains("WMPosterMobilePanel.AssetLibrary", designer, StringComparison.Ordinal);
        Assert.Contains("WMPosterMobilePanel.AssetCrop", designer, StringComparison.Ordinal);
        Assert.Contains("WMMobileEditorSpace.Medium", designer, StringComparison.Ordinal);
        Assert.Contains("WMPosterAssetSelectionSession", designer, StringComparison.Ordinal);
        Assert.Contains("moveable.waitToChangeTarget()", canvasScript, StringComparison.Ordinal);
        Assert.Contains("select(nextSelectedId);", canvasScript, StringComparison.Ordinal);
        Assert.Contains("new ResizeObserver", canvasScript, StringComparison.Ordinal);
        Assert.Contains("viewportResizeObserver?.disconnect();", canvasScript, StringComparison.Ordinal);
        Assert.Contains("EmbeddedMobilePanel=\"@Global.IsMobile\"", designer, StringComparison.Ordinal);
        Assert.Contains("desktop-text-content-open", designer, StringComparison.Ordinal);
        Assert.Contains("FieldLibraryVisibilityChanged", designer, StringComparison.Ordinal);
        Assert.Contains("WMTemplateDesignerSession", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("IWMWatermarkHelper", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("IWMObjectUrlRegistry", designer, StringComparison.Ordinal);
        var workspaceCss = Read("Watermark.Razor/BlazorPages/Mobile/MobileWorkspace.razor.css");
        Assert.Contains("class=\"workspace-designer-host\"", workspace, StringComparison.Ordinal);
        Assert.Contains(".workspace-designer-host", workspaceCss, StringComparison.Ordinal);
        Assert.Contains("position: fixed", workspaceCss, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden", workspaceCss, StringComparison.Ordinal);
        Assert.Contains("aria-current", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("private RenderFragment NavItem", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Message = \"预览已更新\"", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void TextContentEditor_ConstrainsDesktopAndMobileScrollingInsideFixedPanels()
    {
        var editorCss = Read("Watermark.Razor/Components/ExifConfig.razor.css");
        var designerCss = Read("Watermark.Razor/Workspace/Components/WMTemplateDesigner.razor.css");

        Assert.Contains("grid-template-columns: minmax(0, 1.12fr) minmax(300px, .88fr);", editorCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(auto-fit, minmax(168px, 1fr));", editorCss, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", editorCss, StringComparison.Ordinal);
        Assert.Contains("overscroll-behavior-y: contain;", editorCss, StringComparison.Ordinal);
        Assert.Contains("scrollbar-gutter: stable;", editorCss, StringComparison.Ordinal);
        Assert.Contains(".is-embedded-mobile-panel .field-library", editorCss, StringComparison.Ordinal);
        Assert.Contains(".designer-text-content-region .mobile-panel-body", designerCss, StringComparison.Ordinal);
        Assert.Contains(".designer-text-content-region .mobile-panel-body ::deep .text-content-editor", designerCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: minmax(0, 1fr);", designerCss, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatePropertyInspector_ConstrainsDesktopAndMobileScrollingInsideFixedPanels()
    {
        var designer = Read("Watermark.Razor/Workspace/Components/WMTemplateDesigner.razor");
        var designerCss = Read("Watermark.Razor/Workspace/Components/WMTemplateDesigner.razor.css");
        var inspectorCss = Read("Watermark.Razor/Components/Mac/MacSelectionInspector.razor.css");

        Assert.Contains("class=\"mobile-panel-body\"", designer, StringComparison.Ordinal);
        Assert.Contains("<WMTemplatePropertyPanel", designer, StringComparison.Ordinal);
        Assert.Contains(".designer-inspector-region {", designerCss, StringComparison.Ordinal);
        Assert.Contains(".designer-inspector-region .mobile-panel-body {", designerCss, StringComparison.Ordinal);
        Assert.Contains(".designer-inspector-region .mobile-panel-body ::deep .mac-selection-inspector", designerCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: minmax(0, 1fr);", designerCss, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", inspectorCss, StringComparison.Ordinal);
        Assert.Contains("overscroll-behavior-y: contain;", inspectorCss, StringComparison.Ordinal);
        Assert.Contains("scrollbar-gutter: stable;", inspectorCss, StringComparison.Ordinal);
        Assert.Contains(".mobile-inspector-section .selection-inspector-scroll", inspectorCss, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", inspectorCss, StringComparison.Ordinal);
        Assert.Contains("touch-action: pan-y;", inspectorCss, StringComparison.Ordinal);
        Assert.Contains(".inspector-switch-grid", inspectorCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", inspectorCss, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetCropPanel_UsesOnlyBundledPhosphorIcons()
    {
        var cropPanel = Read("Watermark.Razor/Workspace/Components/WMPosterAssetCropPanel.razor");
        var cropPanelCss = Read("Watermark.Razor/Workspace/Components/WMPosterAssetCropPanel.razor.css");
        var cropControls = Read("Watermark.Razor/Workspace/Components/WMCropControls.razor");
        var toolRail = Read("Watermark.Razor/Workspace/Components/WMPosterAssetEditToolRail.razor");
        var icons = new[]
        {
            "arrow-left",
            "arrows-out-line-horizontal",
            "resize",
            "arrows-left-right",
            "crop",
            "arrow-counter-clockwise",
            "arrow-clockwise",
            "flip-horizontal",
            "flip-vertical",
            "angle",
            "arrows-clockwise",
            "drop-half",
            "lock",
            "lock-open"
        };

        Assert.All(icons, icon =>
        {
            Assert.True(
                cropPanel.Contains($"\"{icon}\"", StringComparison.Ordinal)
                || cropControls.Contains($"\"{icon}\"", StringComparison.Ordinal)
                || toolRail.Contains($"\"{icon}\"", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(WmPhosphorIconPaths.Get(icon)));
        });

        Assert.Contains("<WMCropControls", cropPanel, StringComparison.Ordinal);
        Assert.Contains("WMCropSettings", cropPanel, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: minmax(0, 1fr) auto auto;", cropPanelCss, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", cropPanelCss, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(@"\.asset-fit-section\s*\{[^}]*display\s*:\s*none", RegexOptions.Singleline),
            cropPanelCss);
        Assert.Contains("\"white-transparent\"", toolRail, StringComparison.Ordinal);
        Assert.Contains("\"lock-ratio\"", toolRail, StringComparison.Ordinal);
        Assert.DoesNotContain("\"auto-logo\"", toolRail, StringComparison.Ordinal);
        Assert.DoesNotContain("\"cutout\"", toolRail, StringComparison.Ordinal);
        Assert.DoesNotContain("\"effects\"", toolRail, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mask\"", toolRail, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateDesigner_AllPlatformsUseSharedSceneAndVisualsOwnNoRenderingInfrastructure()
    {
        var shared = Read("Watermark.Razor/Workspace/Components/WMTemplateDesigner.razor");
        var desktop = Read("Watermark.Razor/Components/Desktop/WMDesktopTemplateDesigner.razor");
        var desktopPage = Read("Watermark.Razor/BlazorPages/WMDesktopTemplateDesignerPage.razor");
        var desktopWorkspace = Read("Watermark.Razor/BlazorPages/MainViewOSX.razor");
        var canvas = Read("Watermark.Razor/Components/Mac/MacCanvasEditor.razor");
        var canvasJs = Read("Watermark.Razor/wwwroot/js/mac-template-canvas.js");
        var combinedVisuals = shared + desktop + canvas;

        Assert.Contains("<WMTemplateDesigner", desktop, StringComparison.Ordinal);
        Assert.Contains("<WMTemplateDesigner", desktopPage, StringComparison.Ordinal);
        Assert.Contains("<WMTemplateDesigner", desktopWorkspace, StringComparison.Ordinal);
        Assert.Contains("RenderSceneAsync", shared, StringComparison.Ordinal);
        Assert.Contains("WMDesignScenePresentation", canvas, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerationDesignPreviewAsync", combinedVisuals, StringComparison.Ordinal);
        Assert.DoesNotContain("SKBitmap", combinedVisuals, StringComparison.Ordinal);
        Assert.DoesNotContain("Blob(", combinedVisuals, StringComparison.Ordinal);
        Assert.DoesNotContain("createPreviewInteractionVisual", canvasJs, StringComparison.Ordinal);
        Assert.Contains("createLayerInteractionVisual", canvasJs, StringComparison.Ordinal);
        Assert.DoesNotContain("appliedEditorRevision", canvas, StringComparison.Ordinal);
        Assert.Contains("clearPendingInteractionVisual(false)", canvasJs, StringComparison.Ordinal);
        Assert.Contains("coarseResizeDirections", canvasJs, StringComparison.Ordinal);
        Assert.Contains("resizable: true", canvasJs, StringComparison.Ordinal);
        Assert.Contains(".on(\"resize\"", canvasJs, StringComparison.Ordinal);
        Assert.Contains("scalable: false", canvasJs, StringComparison.Ordinal);
        Assert.DoesNotContain(".on(\"scale\"", canvasJs, StringComparison.Ordinal);
        Assert.DoesNotContain("resolveResizeGeometry", canvasJs, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateVisualComponents_DoNotOwnFilesBlobsSkiaOrLayoutAlgorithms()
    {
        var componentRoots = new[]
        {
            Path.Combine(RazorRoot, "Components", "Mac"),
            Path.Combine(RazorRoot, "Workspace", "Components")
        };
        var sources = componentRoots
            .SelectMany(root => Directory.EnumerateFiles(
                root,
                "*.razor",
                SearchOption.AllDirectories))
            .Where(path =>
                path.Contains($"{Path.DirectorySeparatorChar}Mac{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.GetFileName(path).StartsWith("WMTemplate", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join('\n', sources);

        foreach (var forbidden in new[]
                 {
                     "IWMWatermarkHelper",
                     "IWMObjectUrlRegistry",
                     "WMLayoutEngine",
                     "SKBitmap",
                     "SKCanvas",
                     "new Blob(",
                     "File.Read",
                     "File.Write"
                 })
        {
            Assert.DoesNotContain(forbidden, combined, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductBuild_EnablesHeavyImagingAndKeepsEveryCreationModeReachable()
    {
        var project = Read("Watermark.Andorid/Watermark.Andorid.csproj");
        var startup = Read("Watermark.Andorid/MauiProgram.cs");
        var create = Read("Watermark.Razor/BlazorPages/Mobile/MobileCreate.razor");
        var capabilityProvider = Read("Watermark.Andorid/Platforms/Android/WMAndroidImagingCapabilityProvider.cs");
        var enabledProperties = new[]
        {
            "WMImagingMasterEnabled", "WMImagingRawEnabled", "WMImagingStarTrailEnabled",
            "WMImagingMultiFrameEnabled", "WMImagingPng16Enabled", "WMImagingTiff16Enabled"
        };

        Assert.All(enabledProperties, name =>
            Assert.Contains($">true</{name}>", project, StringComparison.Ordinal));
        Assert.Contains("MACCATALYST || WINDOWS || IOS", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("busy || !CanUseMultiFrame", create, StringComparison.Ordinal);
        Assert.Contains("StartAsync(WMWorkspaceMode.MultiFrame)", create, StringComparison.Ordinal);
        Assert.Contains("StartAsync(WMWorkspaceMode.Collage)", create, StringComparison.Ordinal);
        Assert.Contains("WMImagingCapabilityPolicy.Evaluate", capabilityProvider, StringComparison.Ordinal);
    }

    [Fact]
    public void CrossPlatformPreview_UsesCompiledPlanAndSharedWebGlSurface()
    {
        var mobile = Read("Watermark.Razor/BlazorPages/Mobile/MobileWorkspace.razor");
        var desktop = Read("Watermark.Razor/Components/Desktop/WMDesktopPreviewWorkspace.razor");
        var controller = Read("Watermark.Razor/Workspace/WMWorkspaceController.cs");
        var preview = Read("Watermark.Razor/Workspace/WMWorkspacePreviewService.cs");
        var export = Read("Watermark.Razor/Workspace/WMFullResolutionRenderService.cs");
        var surface = Read("Watermark.Razor/Workspace/Components/WMWorkspacePreviewSurface.razor");
        var surfaceStyles = Read("Watermark.Razor/Workspace/Components/WMWorkspacePreviewSurface.razor.css");
        var webGl = Read("Watermark.Razor/wwwroot/js/wm-color-preview.js");
        var manifest = Read("Watermark.Andorid/Platforms/Android/AndroidManifest.xml");

        Assert.Contains("<WMWorkspacePreviewSurface", mobile, StringComparison.Ordinal);
        Assert.Contains("<WMWorkspacePreviewSurface", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=\"@State.PreviewUrl\"", mobile, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=\"@State.PreviewUrl\"", desktop, StringComparison.Ordinal);
        Assert.Contains("WMRenderTarget.SettledPreview()", controller, StringComparison.Ordinal);
        Assert.Contains("WMRenderTarget.InteractiveBase()", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("string? templateId", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("string? templateSnapshotJson", export, StringComparison.Ordinal);
        Assert.Contains("appliedSource", surface, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr);", surfaceStyles, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: minmax(0, 1fr);", surfaceStyles, StringComparison.Ordinal);
        Assert.Matches(new Regex(
            @"\.wm-preview-gpu,\s*\.wm-preview-image\s*\{[^}]*height:\s*100%;[^}]*width:\s*100%;",
            RegexOptions.CultureInvariant | RegexOptions.Singleline), surfaceStyles);
        Assert.Contains("requestAnimationFrame", webGl, StringComparison.Ordinal);
        Assert.Contains("pendingDynamicSnapshot", webGl, StringComparison.Ordinal);
        Assert.Contains("setDynamicState", webGl, StringComparison.Ordinal);
        Assert.Contains("setDynamicState", surface, StringComparison.Ordinal);
        Assert.Contains("webglcontextlost", webGl, StringComparison.Ordinal);
        Assert.Contains("OnFrameMeasured", webGl, StringComparison.Ordinal);
        Assert.Contains("android:hardwareAccelerated=\"true\"", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            RepositoryRoot, "Watermark.Razor", "wwwroot", "js", "wm-color-preview.js")));
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot, "Watermark.Razor", "wwwroot", "js", "mac-color-preview.js")));
    }

    [Fact]
    public void FirstRunPrivacyExperience_IsCompactBrandedAndGatesRoutesBeforeFirstPaint()
    {
        var page = Read("Watermark.Razor/BlazorPages/FirstPage.razor");
        var styles = Read("Watermark.Razor/BlazorPages/FirstPage.razor.css");
        var shell = Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor");
        var gate = Read("Watermark.Razor/Components/Layout/WMPrivacyStartupGate.razor");
        var androidRoutes = Read("Watermark.Andorid/Routes.razor");
        var windowsRoutes = Read("Watermark.Win/BlazorPages/MainView.razor");

        Assert.Contains("privacy-scroll-region", page, StringComparison.Ordinal);
        Assert.Contains("_content/Watermark.Razor/img/app-icon.svg", page, StringComparison.Ordinal);
        Assert.Contains("你的照片", page, StringComparison.Ordinal);
        Assert.Contains("不扫描整个相册", page, StringComparison.Ordinal);
        Assert.Contains("同意并开始使用", page, StringComparison.Ordinal);
        Assert.Contains("https://thankful.top/protocol", page, StringComparison.Ordinal);
        Assert.Contains("attachAndroidMouseDragScroll", page, StringComparison.Ordinal);
        Assert.DoesNotContain("style=", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow-y: auto", styles, StringComparison.Ordinal);
        Assert.Contains("position: fixed", styles, StringComparison.Ordinal);
        Assert.Contains("env(safe-area-inset-bottom)", styles, StringComparison.Ordinal);
        Assert.Contains("@supports (height: 100dvh)", styles, StringComparison.Ordinal);
        Assert.Contains("if (!settingsResolved)", gate, StringComparison.Ordinal);
        Assert.Contains("else if (privacyRequired)", gate, StringComparison.Ordinal);
        Assert.Contains("SettingsService.LoadAsync()", gate, StringComparison.Ordinal);
        Assert.Contains("await SettingsService.SetPrivacyConsentAsync(accepted);", gate, StringComparison.Ordinal);
        Assert.Contains("<WMPrivacyStartupGate>", androidRoutes, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMPrivacyStartupGate>", windowsRoutes, StringComparison.Ordinal);
        Assert.DoesNotContain("showPrivacy", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<FirstPage", shell, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot,
            "Watermark.Razor", "wwwroot", "img", "app-icon.svg")));
    }

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(RazorRoot, "*.razor", SearchOption.AllDirectories);

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(RazorRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string Relative(string path) =>
        Path.GetRelativePath(RepositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Watermark.sln"))) return current.FullName;
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate Watermark.sln for source architecture tests.");
    }
}
