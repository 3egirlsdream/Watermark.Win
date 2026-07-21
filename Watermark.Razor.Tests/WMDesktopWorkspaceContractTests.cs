using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMDesktopWorkspaceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void EmptyPreview_UsesHistoricalDesktopStructureAndCopy()
    {
        var source = Read("Watermark.Razor/Components/Desktop/WMDesktopPreviewWorkspace.razor");

        Assert.Contains("<section class=\"preview-pane\">", source, StringComparison.Ordinal);
        Assert.Contains("<div class=\"preview-empty\">", source, StringComparison.Ordinal);
        Assert.Contains("<IconImage", source, StringComparison.Ordinal);
        Assert.Contains("导入图片开始编辑", source, StringComparison.Ordinal);
        Assert.Contains("@if (HasPreview)", source, StringComparison.Ordinal);
        Assert.Contains("<WMWorkspacePreviewSurface", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaStrip_UsesHistoricalDesktopTilesWithoutMobileMetadata()
    {
        var source = Read("Watermark.Razor/Components/Desktop/WMDesktopMediaStrip.razor");
        var css = Read("Watermark.Razor/Components/Desktop/WMDesktopMediaStrip.razor.css");

        Assert.Contains("media-scroll", source, StringComparison.Ordinal);
        Assert.Contains("media-empty", source, StringComparison.Ordinal);
        Assert.Contains("media-tile", source, StringComparison.Ordinal);
        Assert.Contains("media-check", source, StringComparison.Ordinal);
        Assert.Contains("stack-badge", source, StringComparison.Ordinal);
        Assert.Contains("导入图片后，素材会显示在这里", source, StringComparison.Ordinal);
        Assert.DoesNotContain("已选择", source, StringComparison.Ordinal);
        Assert.Contains("width: 108px", css, StringComparison.Ordinal);
        Assert.Contains("height: 76px", css, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopUsesDedicatedHistoricalPanelsAndDesignerMarkup()
    {
        var page = Read("Watermark.Razor/BlazorPages/MainViewOSX.razor");
        var sidebar = Read("Watermark.Razor/Components/Desktop/WMDesktopModeSidebar.razor");
        var inspector = Read("Watermark.Razor/Components/Desktop/WMDesktopModeInspector.razor");
        var designer = Read("Watermark.Razor/Components/Desktop/WMDesktopTemplateDesigner.razor");

        Assert.Contains("<WMDesktopModeInspector", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMTemplatePanel", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMColorGradePanel", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMMultiFramePanel", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<TemplateSection", sidebar, StringComparison.Ordinal);
        Assert.Contains("ColorSection.Basic", inspector, StringComparison.Ordinal);
        Assert.Contains("ColorSection.Curves", inspector, StringComparison.Ordinal);
        Assert.Contains("class=\"mode-card", inspector, StringComparison.Ordinal);
        Assert.Contains("MacLayerTree", designer, StringComparison.Ordinal);
        Assert.Contains("MacCanvasEditor", designer, StringComparison.Ordinal);
        Assert.Contains("MacSelectionInspector", designer, StringComparison.Ordinal);
        Assert.Contains("WMTemplateDesignerSession", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("IWMWatermarkHelper", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("IWMObjectUrlRegistry", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerationDesignPreviewAsync", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishAsync", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMTemplateDesigner", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("mobile-designer-tabs", designer, StringComparison.Ordinal);
        Assert.DoesNotContain("designer-add-region", designer, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopTemplateEditors_PreventAccidentalInterfaceTextSelection()
    {
        var designerCss = Read("Watermark.Razor/Components/Desktop/WMDesktopTemplateDesigner.razor.css");
        var appliedEditorCss = Read("Watermark.Razor/Components/Desktop/WMDesktopAppliedTemplateEditor.razor.css");

        Assert.Contains(".mac-template-designer ::deep *", designerCss, StringComparison.Ordinal);
        Assert.Contains(".desktop-applied-editor ::deep *", appliedEditorCss, StringComparison.Ordinal);
        Assert.Contains("user-select: none", designerCss, StringComparison.Ordinal);
        Assert.Contains("user-select: none", appliedEditorCss, StringComparison.Ordinal);
        Assert.Contains("::deep input", designerCss, StringComparison.Ordinal);
        Assert.Contains("user-select: text", designerCss, StringComparison.Ordinal);
        Assert.Contains("::deep input", appliedEditorCss, StringComparison.Ordinal);
        Assert.Contains("user-select: text", appliedEditorCss, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopMembership_UsesHistoricalDialogInsteadOfMobilePage()
    {
        var page = Read("Watermark.Razor/BlazorPages/MainViewOSX.razor");
        var dialog = Read("Watermark.Razor/Components/Desktop/WMDesktopMembershipDialog.razor");
        var css = Read("Watermark.Razor/Components/Desktop/WMDesktopMembershipDialog.razor.css");

        Assert.Contains("<WMDesktopMembershipDialog", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMMembershipPage", page, StringComparison.Ordinal);
        Assert.Contains("class=\"mac-vip-dialog\"", dialog, StringComparison.Ordinal);
        Assert.Contains("class=\"vip-plans\"", dialog, StringComparison.Ordinal);
        Assert.Contains("class=\"vip-pay-area\"", dialog, StringComparison.Ordinal);
        Assert.Contains("class=\"checkout-panel\"", dialog, StringComparison.Ordinal);
        Assert.Contains("class=\"vip-actions\"", dialog, StringComparison.Ordinal);
        Assert.Contains("IWMMembershipService", dialog, StringComparison.Ordinal);
        Assert.Contains("IWMAccountService", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("APIHelper", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("IClientInstance", dialog, StringComparison.Ordinal);
        Assert.Contains("width: min(720px, calc(100vw - 48px))", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 220px minmax(0, 1fr)", css, StringComparison.Ordinal);
        Assert.Contains("height: 220px", css, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopToolbar_MoreMenuMatchesHistoricalEntriesAndVisibility()
    {
        var page = Read("Watermark.Razor/BlazorPages/MainViewOSX.razor");
        var toolbar = Read("Watermark.Razor/Components/Desktop/WMDesktopAppToolbar.razor");

        var titles = new[]
        {
            "新建模板", "我的模板", "模板市场", "图标库", "注册账号", "注销账号",
            "设置", "运营看板", "网页版", "提交反馈", "安卓版", "交流群：836325187"
        };
        var previous = -1;
        foreach (var title in titles)
        {
            var current = toolbar.IndexOf($"Title=\"{title}\"", StringComparison.Ordinal);
            Assert.True(current > previous, $"Desktop more-menu entry is missing or out of order: {title}");
            previous = current;
        }

        Assert.DoesNotContain("Title=\"账号\"", toolbar, StringComparison.Ordinal);
        Assert.DoesNotContain("Title=\"退出登录\"", toolbar, StringComparison.Ordinal);
        Assert.Contains("@if (CanOpenDashboard)", toolbar, StringComparison.Ordinal);
        Assert.Contains("OpenCommonDialog.InvokeAsync", toolbar, StringComparison.Ordinal);
        Assert.Contains("CanOpenDashboard=\"@AdminDashboard.IsAuthorized\"", page, StringComparison.Ordinal);
        Assert.Contains("OpenAccountDialog(\"register\")", page, StringComparison.Ordinal);
        Assert.Contains("OpenAccountDialog(\"delete\")", page, StringComparison.Ordinal);
        Assert.Contains("<MImage Height=\"300\" Width=\"300\" Contain Src=\"@commonDialogUrl\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopMoreMenu_UsesDedicatedRoutesForComplexFeaturesAndDesktopDialogsForSmallActions()
    {
        var workspace = Read("Watermark.Razor/BlazorPages/MainViewOSX.razor");
        var auxiliary = Read("Watermark.Razor/BlazorPages/WMDesktopAuxiliaryPage.razor");
        var account = Read("Watermark.Razor/Components/Desktop/WMDesktopAccountDialog.razor");
        var newTemplate = Read("Watermark.Razor/Components/Desktop/WMDesktopNewTemplateDialog.razor");

        Assert.Contains("NavigateDesktopSection(\"templates\")", workspace, StringComparison.Ordinal);
        Assert.Contains("NavigateDesktopSection(\"market\")", workspace, StringComparison.Ordinal);
        Assert.Contains("NavigateDesktopSection(\"resources\")", workspace, StringComparison.Ordinal);
        Assert.Contains("NavigateDesktopSection(\"settings\")", workspace, StringComparison.Ordinal);
        Assert.Contains("NavigateDesktopSection(\"admin\")", workspace, StringComparison.Ordinal);
        Assert.Contains("<WMDesktopAccountDialog", workspace, StringComparison.Ordinal);
        Assert.Contains("<WMDesktopNewTemplateDialog", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMAccountPage", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMSettingsPage", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMResourcesPage", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMAdminDashboardPage", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("showTemplates", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("showMarket", workspace, StringComparison.Ordinal);

        foreach (var route in new[]
                 {
                     "/mac/templates", "/desktop/templates", "/mac/market", "/desktop/market",
                     "/mac/resources", "/desktop/resources", "/mac/settings", "/desktop/settings",
                     "/mac/admin", "/desktop/admin"
                 })
        {
            Assert.Contains($"@page \"{route}\"", auxiliary, StringComparison.Ordinal);
        }

        Assert.Contains("<WMDesktopTemplateLibraryPage", auxiliary, StringComparison.Ordinal);
        Assert.Contains("<WMDesktopTemplateMarket", auxiliary, StringComparison.Ordinal);
        Assert.Contains("<WMDesktopResourceLibraryPage", auxiliary, StringComparison.Ordinal);
        Assert.Contains("<WMDesktopSettingsPage", auxiliary, StringComparison.Ordinal);
        Assert.Contains("<AdminDashboard", auxiliary, StringComparison.Ordinal);
        Assert.Contains("IWMAccountService", account, StringComparison.Ordinal);
        Assert.Contains("IWMTemplateMarketplaceService", newTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("APIHelper", account, StringComparison.Ordinal);
        Assert.DoesNotContain("APIHelper", newTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopTemplateDesigner_HasMacAndWindowsFullscreenRoutes()
    {
        var route = Read("Watermark.Razor/BlazorPages/WMDesktopTemplateDesignerPage.razor");

        Assert.Contains("@page \"/mac/templates/{TemplateId}/edit\"", route, StringComparison.Ordinal);
        Assert.Contains("@page \"/desktop/templates/{TemplateId}/edit\"", route, StringComparison.Ordinal);
        Assert.Contains("<WMDesktopTemplateDesigner", route, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMTemplateDesigner", route, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopTemplateUpload_ResetsFormForEveryOpenedTemplate()
    {
        var library = Read("Watermark.Razor/Components/Desktop/WMDesktopTemplateLibraryPage.razor");
        var upload = Read("Watermark.Razor/Components/Desktop/WMDesktopTemplateUploadDialog.razor");

        Assert.Contains("@key=\"uploadDialogVersion\"", library, StringComparison.Ordinal);
        Assert.Contains("uploadDialogVersion++;", library, StringComparison.Ordinal);
        Assert.Contains("initializedTemplateId", upload, StringComparison.Ordinal);
        Assert.Contains("ResetForm();", upload, StringComparison.Ordinal);
        Assert.Contains("agreed = false;", upload, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool initialized;", upload, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopTemplateMarket_ExposesRecommendationManagementOnlyToOfficialAccounts()
    {
        var market = Read("Watermark.Razor/Components/Desktop/WMDesktopTemplateMarket.razor");
        var service = Read("Watermark.Razor/Workspace/WMTemplateMarketplaceService.cs");
        var api = Read("Watermark.Shared/Models/APIHelper.cs");

        Assert.Contains("AdminDashboard.IsAuthorized", market, StringComparison.Ordinal);
        Assert.Contains("SetRecommendedAsync(item)", market, StringComparison.Ordinal);
        Assert.Contains("下架推荐", market, StringComparison.Ordinal);
        Assert.Contains("加入推荐", market, StringComparison.Ordinal);
        Assert.Contains("AdminAccessPolicy.IsAdmin(Global.CurrentUser)", service, StringComparison.Ordinal);
        Assert.Contains("ToggleWatermarkRecommendationAsync", service, StringComparison.Ordinal);
        Assert.Contains("/api/Watermark/UpdateRecommend", api, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateMarkets_ProvideExplicitCacheBypassingRefresh()
    {
        var desktop = Read("Watermark.Razor/Components/Desktop/WMDesktopTemplateMarket.razor");
        var mobile = Read("Watermark.Razor/BlazorPages/Mobile/MobileTemplates.razor");
        var api = Read("Watermark.Shared/Models/APIHelper.cs");

        Assert.Contains("class=\"market-refresh-command\"", desktop, StringComparison.Ordinal);
        Assert.Contains("RefreshAsync", desktop, StringComparison.Ordinal);
        Assert.Contains("resetScrollPending = true;", desktop, StringComparison.Ordinal);
        Assert.Contains("class=\"templates-refresh\"", mobile, StringComparison.Ordinal);
        Assert.Contains("RefreshMarketAsync", mobile, StringComparison.Ordinal);
        Assert.Contains("ForceRefresh: forceRefresh", desktop, StringComparison.Ordinal);
        Assert.Contains("ForceRefresh: forceRefresh", mobile, StringComparison.Ordinal);
        Assert.Contains("cacheBust", api, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopAuxiliaryNavigationAndMarket_SwitchOnFirstClickAndPageOnScroll()
    {
        var auxiliary = Read("Watermark.Razor/BlazorPages/WMDesktopAuxiliaryPage.razor");
        var shell = Read("Watermark.Razor/Components/Desktop/WMDesktopAuxiliaryShell.razor");
        var market = Read("Watermark.Razor/Components/Desktop/WMDesktopTemplateMarket.razor");
        var marketCss = Read("Watermark.Razor/Components/Desktop/WMDesktopTemplateMarket.razor.css");

        Assert.Contains("activeSection = safeSection;", auxiliary, StringComparison.Ordinal);
        Assert.Contains("Navigation.LocationChanged += OnLocationChanged", auxiliary, StringComparison.Ordinal);
        Assert.Contains("activeSection = ResolveActiveSection();", auxiliary, StringComparison.Ordinal);
        Assert.Contains("async () => await NavigateSection.InvokeAsync", shell, StringComparison.Ordinal);

        Assert.Contains("WMTemplateMarketFeedStore", market, StringComparison.Ordinal);
        Assert.Contains("<MInfiniteScroll", market, StringComparison.Ordinal);
        Assert.Contains("state.NextStart", market, StringComparison.Ordinal);
        Assert.Contains("state.AppendUnique(result.Items)", market, StringComparison.Ordinal);
        Assert.Contains("state.HasMore = result.HasMore", market, StringComparison.Ordinal);
        Assert.Contains("class=\"template-card market-card\"", market, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio:4/3", marketCss, StringComparison.Ordinal);
        Assert.Contains("grid-auto-rows:max-content", marketCss, StringComparison.Ordinal);
        Assert.Contains(".template-meta-row", marketCss, StringComparison.Ordinal);
        Assert.DoesNotContain("content-visibility", marketCss, StringComparison.Ordinal);
        Assert.DoesNotContain("contain-intrinsic-size", marketCss, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopResourceImport_HidesNativeFileInputThroughCssIsolationBoundary()
    {
        var source = Read("Watermark.Razor/Components/Desktop/WMDesktopResourceLibraryPage.razor");
        var css = Read("Watermark.Razor/Components/Desktop/WMDesktopResourceLibraryPage.razor.css");

        Assert.Contains("<InputFile class=\"file-input\"", source, StringComparison.Ordinal);
        Assert.Contains(".import-command ::deep input.file-input", css, StringComparison.Ordinal);
        Assert.Contains("opacity:0", css, StringComparison.Ordinal);
        Assert.Contains("overflow:hidden", css, StringComparison.Ordinal);
        Assert.Contains(".resource-card.is-font{grid-template-columns:minmax(0,1fr) auto}", css, StringComparison.Ordinal);
        Assert.Contains("white-space:nowrap", css, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopVisualComponents_DoNotOwnFilesBlobsOrImagingEngines()
    {
        var desktopRoot = Path.Combine(RepositoryRoot, "Watermark.Razor", "Components", "Desktop");
        var sources = Directory.EnumerateFiles(desktopRoot, "*.razor", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join('\n', sources);

        Assert.DoesNotContain("IWMImageStackEngine", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("IWMColorGradeOperationProcessor", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("IWMWatermarkHelper", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("IWMObjectUrlRegistry", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerationDesignPreviewAsync", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishAsync", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("URL.createObjectURL", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Read", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Write", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Create", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportPanel_UsesSharedModernChoicesAndLiveAccessibleQualitySlider()
    {
        var panel = Read("Watermark.Razor/Workspace/Components/WMExportPanel.razor");
        var css = Read("Watermark.Razor/Workspace/Components/WMExportPanel.razor.css");
        var mobile = Read("Watermark.Razor/BlazorPages/Mobile/MobileWorkspace.razor");
        var desktop = Read("Watermark.Razor/Components/Desktop/WMDesktopModeInspector.razor");

        Assert.Contains("wm-export-format-grid", panel, StringComparison.Ordinal);
        Assert.Contains("wm-export-resolution-grid", panel, StringComparison.Ordinal);
        Assert.Contains("wm-export-destination-grid", panel, StringComparison.Ordinal);
        Assert.Contains("type=\"range\"", panel, StringComparison.Ordinal);
        Assert.Contains("@oninput=\"ChangeQualityAsync\"", panel, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"@AriaPressed", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("<select", panel, StringComparison.Ordinal);
        Assert.Contains("--wm-range-progress", css, StringComparison.Ordinal);
        Assert.Contains("input::-webkit-slider-runnable-track", css, StringComparison.Ordinal);
        Assert.Contains("height: 42px", css, StringComparison.Ordinal);
        Assert.Contains("workspace-export-drawer-handle", mobile, StringComparison.Ordinal);
        Assert.Contains("<WMExportPanel Expanded=\"true\"", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void CropCanvas_UsesVendoredCropperJsForMouseTouchAndKeyboardInteraction()
    {
        var script = Read("Watermark.Razor/wwwroot/js/wm-crop-canvas.js");
        var component = Read("Watermark.Razor/Workspace/Components/WMCropCanvas.razor");
        var css = Read("Watermark.Razor/Workspace/Components/WMCropCanvas.razor.css");
        var vendor = Read("Watermark.Razor/wwwroot/vendor/cropperjs/cropper.esm.min.js");
        var license = Read("Watermark.Razor/wwwroot/vendor/cropperjs/LICENSE");

        Assert.Contains("from '../vendor/cropperjs/cropper.esm.min.js'", script, StringComparison.Ordinal);
        Assert.Contains("new Cropper(source", script, StringComparison.Ordinal);
        Assert.Contains("<cropper-selection movable resizable keyboard precise outlined", script, StringComparison.Ordinal);
        Assert.Contains("selection.addEventListener(EVENT_CHANGE", script, StringComparison.Ordinal);
        Assert.Contains("cropImage.addEventListener(EVENT_TRANSFORM", script, StringComparison.Ordinal);
        Assert.Contains("if (!ready || syncing || event.target !== selection) return;", script, StringComparison.Ordinal);
        Assert.Contains("if (!ready || syncing || event.target !== cropImage) return;", script, StringComparison.Ordinal);
        Assert.Contains("await waitForCropperImageLayout();", script, StringComparison.Ordinal);
        Assert.Contains("await new Promise(resolve => requestAnimationFrame(resolve));", script, StringComparison.Ordinal);
        Assert.Contains("!cropImage.$isReady", script, StringComparison.Ordinal);
        Assert.Contains("await cropImage.$nextTick();", script, StringComparison.Ordinal);
        Assert.Contains("function applySourceCoordinateSize()", script, StringComparison.Ordinal);
        Assert.Contains("void cropImage.offsetWidth;", script, StringComparison.Ordinal);
        Assert.Contains("syncFromSettings(true);", script, StringComparison.Ordinal);
        Assert.Contains("if (revealWhenSynchronized)", script, StringComparison.Ordinal);
        Assert.Contains("const imageStyleObserver = new MutationObserver", script, StringComparison.Ordinal);
        Assert.Contains("cropImage.offsetWidth === sourceWidth", script, StringComparison.Ordinal);
        Assert.Contains("imageStyleObserver.disconnect();", script, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", script, StringComparison.Ordinal);
        Assert.Contains("deriveSettings", script, StringComparison.Ordinal);
        Assert.Contains("normalize(candidate", script, StringComparison.Ordinal);
        Assert.Contains("geometryMatches(candidate, normalized)", script, StringComparison.Ordinal);
        Assert.Contains("selectionFitsCanvas(requested)", script, StringComparison.Ordinal);
        Assert.Contains("selectionRectForSettings(normalized", script, StringComparison.Ordinal);
        Assert.Contains("imageMatrixForSettingsAtSelection(normalized", script, StringComparison.Ordinal);
        Assert.Contains("sourceToPlane(settings", script, StringComparison.Ordinal);
        Assert.Contains("cropper?.destroy()", script, StringComparison.Ordinal);
        Assert.Contains("host.replaceChildren()", script, StringComparison.Ordinal);
        Assert.Contains("source.remove()", script, StringComparison.Ordinal);
        Assert.Contains("time - lastCallbackTime < 32", script, StringComparison.Ordinal);
        Assert.Contains("scheduleIdleFinalize()", script, StringComparison.Ordinal);
        Assert.Contains("event.stopPropagation()", script, StringComparison.Ordinal);
        Assert.Contains("cancelIdleFinalize();", script, StringComparison.Ordinal);
        Assert.Contains("OnCropGestureAsync', snapshot, revision", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$toCanvas", script, StringComparison.Ordinal);
        Assert.Contains("class=\"wm-cropper-host\"", component, StringComparison.Ordinal);
        Assert.Contains("::deep cropper-selection", css, StringComparison.Ordinal);
        Assert.Contains("@media (pointer:coarse)", css, StringComparison.Ordinal);
        Assert.Contains("height:44px", css, StringComparison.Ordinal);
        Assert.Contains("Cropper.js v2.1.1", vendor, StringComparison.Ordinal);
        Assert.Contains("The MIT License (MIT)", license, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopTemplateCards_UseDedicatedAccessibleApplyButtons()
    {
        var source = Read("Watermark.Razor/Components/Desktop/WMDesktopModeSidebar.razor");
        var css = Read("Watermark.Razor/Components/Desktop/WMDesktopModeSidebar.razor.css");

        Assert.Contains("class=\"template-card-apply\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"应用模板 @item.Canvas.Name\"", source, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => TemplateSelected.InvokeAsync(item)\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"button\"", source, StringComparison.Ordinal);
        Assert.Contains(".template-card-apply:focus-visible", css, StringComparison.Ordinal);
    }

    [Fact]
    public void AppliedTemplateEditor_BindsRenderedPreviewUrlInsteadOfLiteralText()
    {
        var source = Read("Watermark.Razor/Components/Desktop/WMDesktopAppliedTemplateEditor.razor");

        Assert.Contains("PreviewUrl=\"@State.PreviewUrl\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewUrl=\"State.PreviewUrl\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSources_DoNotReferenceDesktopComponents()
    {
        var mobileRoot = Path.Combine(RepositoryRoot, "Watermark.Razor", "BlazorPages", "Mobile");
        var mobile = string.Join('\n', Directory.EnumerateFiles(mobileRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));

        Assert.DoesNotContain("Components.Desktop", mobile, StringComparison.Ordinal);
        Assert.DoesNotContain("WMDesktop", mobile, StringComparison.Ordinal);
        Assert.DoesNotContain("mac-desktop", mobile, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopDialogPages_AcceptExplicitReturnUrlWithoutLosingQueryRouting()
    {
        foreach (var path in new[]
                 {
                     "Watermark.Razor/BlazorPages/WMMembershipPage.razor",
                     "Watermark.Razor/BlazorPages/WMAccountPage.razor",
                     "Watermark.Razor/BlazorPages/WMSettingsPage.razor",
                     "Watermark.Razor/BlazorPages/WMResourcesPage.razor",
                     "Watermark.Razor/BlazorPages/WMAdminDashboardPage.razor"
                 })
        {
            var source = Read(path);
            Assert.Contains("[Parameter] public string? ReturnUrl", source, StringComparison.Ordinal);
            Assert.Contains("[SupplyParameterFromQuery(Name = \"returnUrl\")] private string? QueryReturnUrl", source, StringComparison.Ordinal);
            Assert.DoesNotContain("[SupplyParameterFromQuery(Name = \"returnUrl\")] public string? ReturnUrl", source, StringComparison.Ordinal);
        }
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

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

        throw new DirectoryNotFoundException("Could not locate Watermark.sln for desktop contract tests.");
    }
}
