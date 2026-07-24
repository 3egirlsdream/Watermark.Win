using System.Text.RegularExpressions;
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
    public void MobileTemplateDesigner_UsesPageBackCanonicalPreviewAndResizableDrawer()
    {
        var designer = Read("Watermark.Razor/Workspace/Components/WMTemplateDesigner.razor");
        var designerCss = Read("Watermark.Razor/Workspace/Components/WMTemplateDesigner.razor.css");
        var drawerJs = Read("Watermark.Razor/wwwroot/js/wm-template-designer.js");
        var sliderCss = Read("Watermark.Razor/Components/Mac/MacSlider.razor.css");
        var shell = Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor");
        var workspace = Read("Watermark.Razor/BlazorPages/Mobile/MobileWorkspace.razor");
        var controller = Read("Watermark.Razor/Workspace/WMWorkspaceController.cs");

        Assert.Contains("TemplateLibrary.GetOrRefreshAsync()", designer, StringComparison.Ordinal);
        Assert.Contains("Global.InitFonts([loadedCanvas])", designer, StringComparison.Ordinal);
        Assert.Contains("CanNavigateAwayAsync", designer, StringComparison.Ordinal);
        var backHandler = designer[designer.IndexOf("public async Task HandleBackAsync()", StringComparison.Ordinal)..
            designer.IndexOf("public Task<bool> CanNavigateAwayAsync()", StringComparison.Ordinal)];
        Assert.DoesNotContain("CancelTransaction", backHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("Select(null)", backHandler, StringComparison.Ordinal);
        Assert.Contains("--mobile-designer-drawer-height", designerCss, StringComparison.Ordinal);
        Assert.Contains(".wm-template-designer ::deep *", designerCss, StringComparison.Ordinal);
        Assert.Contains("user-select: none", designerCss, StringComparison.Ordinal);
        Assert.Contains("::deep input", designerCss, StringComparison.Ordinal);
        Assert.Contains("user-select: text", designerCss, StringComparison.Ordinal);
        Assert.Contains("pointerdown", drawerJs, StringComparison.Ordinal);
        Assert.Contains("setPointerCapture", drawerJs, StringComparison.Ordinal);
        Assert.Contains("touch-action: pan-y", sliderCss, StringComparison.Ordinal);
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
        Assert.Contains("scalable: true", canvasJs, StringComparison.Ordinal);
        Assert.Contains(".on(\"scale\"", canvasJs, StringComparison.Ordinal);
        Assert.DoesNotContain("resizable: true", canvasJs, StringComparison.Ordinal);
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
