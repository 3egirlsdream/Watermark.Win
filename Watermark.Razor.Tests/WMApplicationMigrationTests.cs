using System.Text.RegularExpressions;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Masa.Blazor;
using Watermark.Razor.Components.Compatibility;
using Watermark.Razor.Workspace;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMApplicationMigrationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("/profile", "/profile")]
    [InlineData("/workspace/session-1?tab=color", "/workspace/session-1?tab=color")]
    [InlineData("/settings?section=update", "/settings?section=update")]
    [InlineData("https://example.com", "/profile")]
    [InlineData("//example.com/path", "/profile")]
    [InlineData("/../outside", "/profile")]
    [InlineData("/workspace\\outside", "/profile")]
    public void ReturnUrl_AllowsOnlySafeInternalAbsolutePaths(string candidate, string expected) =>
        Assert.Equal(expected, WMReturnUrl.Normalize(candidate));

    [Fact]
    public void HostNavigationBridge_RejectsExternalNavigation()
    {
        var bridge = new WMHostNavigationBridge();
        string? route = null;
        bridge.NavigationRequested += value => route = value;

        bridge.Navigate("https://example.com");

        Assert.Equal("/create", route);
    }

    [Fact]
    public void NavigationHistory_SecondaryPagesReturnOneLevelAtATime()
    {
        var navigation = new TestNavigationManager("/profile");
        using var history = new WMNavigationHistory(navigation);

        history.NavigateTo("/settings?returnUrl=/profile");
        history.NavigateTo("/membership?returnUrl=/settings%3FreturnUrl%3D%2Fprofile");

        Assert.Equal(2, history.BackDepth);
        history.GoBack("/profile");
        Assert.Equal("/settings?returnUrl=/profile", history.CurrentPathAndQuery);
        Assert.Equal(1, history.BackDepth);

        history.GoBack("/profile");
        Assert.Equal("/profile", history.CurrentPathAndQuery);
        Assert.Equal(0, history.BackDepth);
    }

    [Fact]
    public void NavigationHistory_RootNavigationClearsSecondaryHistory()
    {
        var navigation = new TestNavigationManager("/profile");
        using var history = new WMNavigationHistory(navigation);

        history.NavigateTo("/settings");
        history.NavigateRoot("/templates");

        Assert.Equal("/templates", history.CurrentPathAndQuery);
        Assert.Equal(0, history.BackDepth);
    }

    [Fact]
    public void NavigationHistory_ReplaceWithPreviousPageDoesNotLeaveDuplicateBackEntry()
    {
        var navigation = new TestNavigationManager("/profile");
        using var history = new WMNavigationHistory(navigation);

        history.NavigateTo("/account/login?returnUrl=/profile");
        history.NavigateTo("/account/recover?returnUrl=/profile");
        history.NavigateTo("/account/login?returnUrl=/profile", replace: true);

        Assert.Equal(1, history.BackDepth);
        history.GoBack("/profile");
        Assert.Equal("/profile", history.CurrentPathAndQuery);
    }

    [Theory]
    [InlineData("avatar.png", "https://cdn.thankful.top/avatar.png")]
    [InlineData("http://cdn.thankful.top/avatar.png", "https://cdn.thankful.top/avatar.png")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("../avatar.png", null)]
    public void AvatarUrl_IsNormalizedAndRejectsUnsafeValues(string value, string? expected)
    {
        var method = typeof(WMAccountService).GetMethod("ResolveAvatarUrl", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, [value]));
    }

    [Fact]
    public void NewApplicationRoutes_HaveExactlyOneOwner()
    {
        var routes = RazorFiles()
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "@page\\s+\"([^\"]+)\"")
                .Select(match => (Route: match.Groups[1].Value, File: Relative(path))))
            .GroupBy(item => item.Route, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.File).ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var route in new[]
                 {
                     "/", "/create", "/templates", "/profile", "/settings", "/settings/resources",
                     "/settings/imaging", "/membership", "/account/login", "/account/register",
                     "/account/recover", "/account/change-password", "/account/delete",
                     "/admin/dashboard", "/workspace/{SessionId}"
                 })
        {
            Assert.True(routes.TryGetValue(route, out var owners), $"Missing route {route}");
            Assert.Single(owners!);
        }

        Assert.DoesNotContain("/test", routes.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigratedPages_DoNotAccessLegacyInfrastructure()
    {
        var files = new[]
        {
            "Watermark.Razor/BlazorPages/WMAccountPage.razor",
            "Watermark.Razor/BlazorPages/WMSettingsPage.razor",
            "Watermark.Razor/BlazorPages/WMResourcesPage.razor",
            "Watermark.Razor/BlazorPages/WMMembershipPage.razor",
            "Watermark.Razor/BlazorPages/WMAdminDashboardPage.razor",
            "Watermark.Razor/BlazorPages/Mobile/MobileProfile.razor",
            "Watermark.Razor/BlazorPages/Mobile/MobileTemplates.razor"
        };
        var forbidden = new[] { "PageStack", "APIHelper", "IClientInstance", "Global.CurrentUser", "Directory.", "File." };

        foreach (var file in files)
        {
            var source = Read(file);
            Assert.All(forbidden, token => Assert.DoesNotContain(token, source, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void LegacyPageTypesAndLayouts_AreDeleted()
    {
        foreach (var relative in new[]
                 {
                     "Watermark.Razor/BlazorPages/IndexView.razor",
                     "Watermark.Razor/BlazorPages/SettingPage.razor",
                     "Watermark.Razor/BlazorPages/Login.razor",
                     "Watermark.Razor/BlazorPages/SignUp.razor",
                     "Watermark.Razor/BlazorPages/Logout.razor",
                     "Watermark.Razor/BlazorPages/DailyLogRecord.razor",
                     "Watermark.Razor/BlazorPages/TestView.razor",
                     "Watermark.Razor/Components/Layout/MainLayout.razor",
                     "Watermark.Razor/Components/Layout/MainLayout2.razor",
                     "Watermark.Razor/Components/Layout/MobileMainLayout.razor",
                     "Watermark.Win/Views/Setting.xaml",
                     "Watermark.Win/BlazorPages/Setting.razor"
                 })
            Assert.False(File.Exists(Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar))), relative);

        var source = string.Join('\n', SourceFiles().Select(File.ReadAllText));
        Assert.DoesNotContain("PageStackNavController", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<PPageStack", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidWorkspaceImport_UsesPhotoFirstPickerWithGalleryFallback()
    {
        var activity = Read("Watermark.Andorid/Platforms/Android/MainActivity.cs");
        var picker = Read("Watermark.Andorid/Platforms/Android/WMAndroidPhotoPicker.cs");
        var startup = Read("Watermark.Andorid/MauiProgram.cs");

        Assert.Contains("android.provider.action.PICK_IMAGES", activity, StringComparison.Ordinal);
        Assert.Contains("android.provider.extra.PICK_IMAGES_MAX", activity, StringComparison.Ordinal);
        Assert.Contains("Intent.ActionPick", activity, StringComparison.Ordinal);
        Assert.Contains("Intent.ActionOpenDocument", activity, StringComparison.Ordinal);
        Assert.Contains("ClipData", activity, StringComparison.Ordinal);
        Assert.Contains("class WMAndroidPhotoPicker : IWMPhotoPicker", picker, StringComparison.Ordinal);
        Assert.DoesNotContain("FilePicker", picker, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IWMPhotoPicker, WMAndroidPhotoPicker>()", startup, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileAppShell_FillsUnsupportedDynamicViewportAndKeepsEmptyPagesContinuous()
    {
        var shellCss = Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor.css");
        var templatesCss = Read("Watermark.Razor/BlazorPages/Mobile/MobileTemplates.razor.css");
        var shellRule = Regex.Match(shellCss, @"\.wm-app-shell\s*\{(?<body>[^}]*)\}")
            .Groups["body"].Value;

        Assert.Contains("<MApp Class=\"wm-app-host\">", Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor"), StringComparison.Ordinal);
        Assert.Contains("<div class=\"wm-app-shell\">", Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor"), StringComparison.Ordinal);
        Assert.Contains("<main class=\"wm-shell-main\">", Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor"), StringComparison.Ordinal);
        Assert.DoesNotContain("<MMain", Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor"), StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: minmax(0, 1fr) auto", shellCss, StringComparison.Ordinal);
        Assert.Contains("height: 100vh", shellRule, StringComparison.Ordinal);
        Assert.Contains("max-height: 100vh", shellRule, StringComparison.Ordinal);
        Assert.DoesNotContain("inset:", shellRule, StringComparison.Ordinal);
        Assert.DoesNotContain("position: fixed", shellRule, StringComparison.Ordinal);
        Assert.Contains("--wm-shell-safe-bottom: min(env(safe-area-inset-bottom, 0px), 34px)", shellCss, StringComparison.Ordinal);
        Assert.Contains("grid-row: 2", shellCss, StringComparison.Ordinal);
        Assert.Contains("position: relative", shellCss, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", shellCss, StringComparison.Ordinal);
        Assert.Contains("-webkit-overflow-scrolling: touch", shellCss, StringComparison.Ordinal);
        Assert.Contains("@supports (height: 100dvh)", shellCss, StringComparison.Ordinal);
        Assert.Contains("max-height: 100dvh", shellCss, StringComparison.Ordinal);
        Assert.DoesNotContain("height: calc(100vh - 74px", shellCss, StringComparison.Ordinal);
        Assert.Contains("background: var(--wm-page)", shellCss, StringComparison.Ordinal);
        Assert.Contains("background: var(--wm-page)", templatesCss, StringComparison.Ordinal);
        Assert.Contains("display: flex", templatesCss, StringComparison.Ordinal);
        Assert.Contains("flex: 1 0 auto", templatesCss, StringComparison.Ordinal);
    }

    [Fact]
    public void IosWebView_OwnsSafeAreasWithoutASecondNativeBottomInset()
    {
        var mainPage = Read("Watermark.Andorid/MainPage.xaml.cs");
        var index = Read("Watermark.Andorid/wwwroot/index.html");
        var appCss = Read("Watermark.Andorid/wwwroot/css/app.css");

        Assert.Contains("#if IOS", mainPage, StringComparison.Ordinal);
        Assert.Contains(".SetUseSafeArea(false)", mainPage, StringComparison.Ordinal);
        Assert.Contains("ContentInsetAdjustmentBehavior.Never", mainPage, StringComparison.Ordinal);
        Assert.Contains("ContentInset = UIKit.UIEdgeInsets.Zero", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("status-bar-safe-area", index, StringComparison.Ordinal);
        Assert.DoesNotContain("status-bar-safe-area", appCss, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileAppShell_UsesPhosphorSvgIconsWithRegularAndLightWeights()
    {
        var shell = Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor");
        var shellCss = Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor.css");
        var icon = Read("Watermark.Razor/Components/Compatibility/WmIcon.razor");
        var paths = Read("Watermark.Razor/Components/Compatibility/WmPhosphorIconPaths.g.cs");

        Assert.Contains("<WmIcon Icon=\"image-square\"", shell, StringComparison.Ordinal);
        Assert.Contains("<WmIcon Icon=\"squares-four\"", shell, StringComparison.Ordinal);
        Assert.Contains("<WmIcon Icon=\"user\"", shell, StringComparison.Ordinal);
        Assert.Contains("? \"regular\" : \"light\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<M" + "Icon", shell, StringComparison.Ordinal);
        Assert.Contains("shape-rendering=\"geometricPrecision\"", icon, StringComparison.Ordinal);
        Assert.Contains("\"image-square\" => useLightWeight", paths, StringComparison.Ordinal);
        Assert.Contains("\"squares-four\" => useLightWeight", paths, StringComparison.Ordinal);
        Assert.Contains("\"user\" => useLightWeight", paths, StringComparison.Ordinal);
        Assert.DoesNotContain("drop-shadow", shellCss, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileProfile_RendersTheVersionAsARazorExpression()
    {
        var profile = Read("Watermark.Razor/BlazorPages/Mobile/MobileProfile.razor");

        Assert.Contains("版本 @(Updates.State.CurrentVersion)", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("v@Updates.State.CurrentVersion", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void MembershipSurfaces_UseTheSharedCrownMark()
    {
        var profile = Read("Watermark.Razor/BlazorPages/Mobile/MobileProfile.razor");
        var membership = Read("Watermark.Razor/BlazorPages/WMMembershipPage.razor");
        var mark = Read("Watermark.Razor/Components/Compatibility/WmMembershipMark.razor");

        Assert.Contains("<WmMembershipMark Compact />", profile, StringComparison.Ordinal);
        Assert.Contains("<WmMembershipMark />", membership, StringComparison.Ordinal);
        Assert.Contains("Icon=\"crown\"", mark, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"diamond\"", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"diamond\"", membership, StringComparison.Ordinal);
    }

    [Fact]
    public void IconSystem_UsesOneSharedPhosphorDependencyWithoutMdiFallbacks()
    {
        var forbidden = new[]
        {
            "mdi" + "-",
            "<M" + "Icon",
            "materialdesign" + "icons",
            "@mdi" + "/font",
            "WmIcons." + "Material"
        };
        var offenders = UiSourceFiles()
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .SelectMany(item => forbidden
                .Where(token => item.Source.Contains(token, StringComparison.OrdinalIgnoreCase))
                .Select(token => $"{Relative(item.Path)} contains {token}"))
            .ToArray();

        Assert.Empty(offenders);
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot, "Watermark.Razor", "Components", "Layout", "WMPhosphorIcon.razor")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "Watermark.Andorid", "wwwroot", "css", "materialdesign")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "Watermark.Win", "wwwroot", "css", "materialdesign")));

        foreach (var registration in new[]
        {
            "Watermark.Andorid/MauiProgram.cs",
            "Watermark.Win/Models/IocHelper.cs",
            "Watermark.Web/Watermark.Web/Program.cs",
            "Watermark.Web/Watermark.Web.Client/Program.cs"
        })
            Assert.Contains("AddWatermarkMasaBlazor", Read(registration), StringComparison.Ordinal);
    }

    [Fact]
    public void MasaIconAliases_AllResolveToSharedPhosphorSvgPaths()
    {
        var aliases = WmPhosphorMasaIcons.CreateAliases();
        var iconProperties = typeof(IconAliases)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.PropertyType == typeof(Icon))
            .ToArray();
        var unresolved = iconProperties
            .Where(property => property.GetValue(aliases)?.ToString()?.StartsWith(
                typeof(SvgPath).FullName + ":", StringComparison.Ordinal) != true)
            .Select(property => property.Name)
            .ToArray();

        Assert.NotEmpty(iconProperties);
        Assert.Empty(unresolved);
    }

    [Fact]
    public void MobileMarketplace_UsesCategoriesPagedLoadingAndLatestWinsCancellation()
    {
        var templates = Read("Watermark.Razor/BlazorPages/Mobile/MobileTemplates.razor");
        var templatesCss = Read("Watermark.Razor/BlazorPages/Mobile/MobileTemplates.razor.css");
        var paging = Read("Watermark.Razor/Workspace/WMTemplateMarketPaging.cs");

        Assert.Contains("MInfiniteScroll", templates, StringComparison.Ordinal);
        Assert.DoesNotContain("MPullRefresh", templates, StringComparison.Ordinal);
        Assert.Contains("WMTemplateMarketCategory.Recommended", templates, StringComparison.Ordinal);
        Assert.Contains("WMTemplateMarketCategory.Popular", templates, StringComparison.Ordinal);
        Assert.Contains("WMTemplateMarketCategory.Latest", templates, StringComparison.Ordinal);
        Assert.Contains("WMTemplateMarketCategory.Collage", templates, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource", templates, StringComparison.Ordinal);
        Assert.Contains("@if (marketState.HasMore)", templates, StringComparison.Ordinal);
        Assert.Contains("resetInfinitePending = state.HasMore;", templates, StringComparison.Ordinal);
        Assert.DoesNotContain("resetInfinitePending = true;", templates, StringComparison.Ordinal);
        Assert.Contains("class=\"market-featured\"", templates, StringComparison.Ordinal);
        Assert.Contains("title=\"已添加\"", templates, StringComparison.Ordinal);
        Assert.DoesNotContain(">已添加</span>", templates, StringComparison.Ordinal);
        Assert.DoesNotContain("<M" + "Icon", templates, StringComparison.Ordinal);
        Assert.DoesNotContain("market-featured-summary", templates, StringComparison.Ordinal);
        Assert.Contains("? \"搜索模板\"", templates, StringComparison.Ordinal);
        Assert.DoesNotContain("backdrop-filter", templatesCss, StringComparison.Ordinal);
        Assert.Contains("object-fit: contain", templatesCss, StringComparison.Ordinal);
        Assert.Contains("border-radius: 999px", templatesCss, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid #9dbfff", templatesCss, StringComparison.Ordinal);
        Assert.Contains("SelectFeaturedMarketItem(state);", templates, StringComparison.Ordinal);
        Assert.Contains("WMTemplateMarketFeatureSelector.WithoutFeatured", templates, StringComparison.Ordinal);
        Assert.Contains("Icon=\"tray-arrow-down\"", templates, StringComparison.Ordinal);
        Assert.Contains("class=\"template-card market-card\"", templates, StringComparison.Ordinal);
        Assert.Contains("<span class=\"creator-line\"><small>本地模板</small></span>", templates, StringComparison.Ordinal);
        Assert.Contains("data-template-id=\"@featuredItem.WatermarkId\"", templates, StringComparison.Ordinal);
        Assert.Contains("flex: 1", templatesCss, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchAsync(search, 1, 120)", templates, StringComparison.Ordinal);
        Assert.Contains("GetMarketTemplatesAsync", paging, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumSourceRequests", paging, StringComparison.Ordinal);
        Assert.Contains("SourceRequestCount: 1", paging, StringComparison.Ordinal);
        Assert.Contains("AppendUnique", paging, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceLibrary_LoadsLocalFontsAndKeepsNativeFileInputInsideTheImportButton()
    {
        var page = Read("Watermark.Razor/BlazorPages/WMResourcesPage.razor");
        var css = Read("Watermark.Razor/BlazorPages/WMResourcesPage.razor.css");
        var services = Read("Watermark.Razor/Workspace/WMApplicationServices.cs");

        Assert.Contains("resources-file-input", page, StringComparison.Ordinal);
        Assert.Contains("item.PreviewUrl", page, StringComparison.Ordinal);
        Assert.Contains("::deep .resources-file-input", css, StringComparison.Ordinal);
        Assert.Contains("ReadInstalledFonts", services, StringComparison.Ordinal);
        Assert.Contains("CreateFontPreviewDataUrl", services, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(item => item.Installed)", services, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidAppShell_MapsMouseDragToVerticalScrollWithoutChangingDesktopInput()
    {
        var layout = Read("Watermark.Razor/Components/Layout/WMAppShellLayout.razor");
        var interop = Read("Watermark.Razor/wwwroot/js/wm-app-shell-scroll.js");

        Assert.Contains("attachAndroidMouseDragScroll", layout, StringComparison.Ordinal);
        Assert.Contains("/Android/i.test(navigator.userAgent)", interop, StringComparison.Ordinal);
        Assert.Contains("event.pointerType !== \"mouse\"", interop, StringComparison.Ordinal);
        Assert.Contains("scroller.scrollTop = startScrollTop - deltaY", interop, StringComparison.Ordinal);
        Assert.Contains("event.stopImmediatePropagation()", interop, StringComparison.Ordinal);
    }

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "Watermark.Razor"), "*.razor", SearchOption.AllDirectories)
            .Where(NotGenerated);

    private static IEnumerable<string> SourceFiles() =>
        new[] { "Watermark.Razor", "Watermark.Andorid", "Watermark.Win", "Watermark.Shared" }
            .SelectMany(root => Directory.EnumerateFiles(Path.Combine(RepositoryRoot, root), "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(NotGenerated);

    private static IEnumerable<string> UiSourceFiles() =>
        new[] { "Watermark.Razor", "Watermark.Andorid", "Watermark.Win", "Watermark.Shared", "Watermark.Web" }
            .SelectMany(root => Directory.EnumerateFiles(Path.Combine(RepositoryRoot, root), "*.*", SearchOption.AllDirectories))
            .Where(path => new[] { ".razor", ".cs", ".html", ".css", ".csproj" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(NotGenerated);

    private static bool NotGenerated(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

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
        throw new DirectoryNotFoundException("Could not locate Watermark.sln.");
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string initialPath) =>
            Initialize("https://app.local/", $"https://app.local{initialPath}");

        protected override void NavigateToCore(string uri, bool forceLoad) =>
            NavigateToCore(uri, new NavigationOptions { ForceLoad = forceLoad });

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(isInterceptedLink: false);
        }
    }
}
