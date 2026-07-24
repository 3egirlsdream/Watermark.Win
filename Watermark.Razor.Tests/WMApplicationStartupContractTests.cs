using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMApplicationStartupContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void PrivacyGate_StartsApplicationOnlyAfterConsent()
    {
        var gate = Read("Watermark.Razor/Components/Layout/WMPrivacyStartupGate.razor");

        Assert.Contains("settings.PrivacyAccepted", gate, StringComparison.Ordinal);
        Assert.Contains("if (accepted)", gate, StringComparison.Ordinal);
        Assert.Contains("StartApplicationInitialization();", gate, StringComparison.Ordinal);
        Assert.Contains("await Task.Yield();", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("await InitializeApplicationSafelyAsync();", gate, StringComparison.Ordinal);
        Assert.Equal(1, Count(gate, "StartupService.InitializeAfterPrivacyConsentAsync()"));
    }

    [Fact]
    public void AndroidColdStart_UsesOneBrandedSurfaceAndDefersLegacyAssets()
    {
        var project = Read("Watermark.Andorid/Watermark.Andorid.csproj");
        var app = Read("Watermark.Andorid/App.xaml");
        var activity = Read("Watermark.Andorid/Platforms/Android/MainActivity.cs");
        var host = Read("Watermark.Andorid/wwwroot/index.html");
        var hostStyles = Read("Watermark.Andorid/wwwroot/css/app.css");
        var gateStyles = Read("Watermark.Razor/Components/Layout/WMPrivacyStartupGate.razor.css");

        Assert.Contains("Color=\"#FAFAFA\" BaseSize=\"108,108\"", project, StringComparison.Ordinal);
        Assert.Contains("<Color x:Key=\"PageBackgroundColor\">#FAFAFA</Color>", app, StringComparison.Ordinal);
        Assert.Contains("Color.ParseColor(\"#FAFAFA\")", activity, StringComparison.Ordinal);
        Assert.Contains("class=\"wm-startup-splash\"", host, StringComparison.Ordinal);
        Assert.Contains("_content/Watermark.Razor/img/app-icon.svg", host, StringComparison.Ordinal);
        Assert.Contains("css/app.css?v=20260724", host, StringComparison.Ordinal);
        Assert.Contains("Litograph.styles.css?v=20260724", host, StringComparison.Ordinal);
        Assert.Contains("height: 108px", hostStyles, StringComparison.Ordinal);
        Assert.Contains("height: 108px", gateStyles, StringComparison.Ordinal);
        Assert.Contains("background: #fafafa", hostStyles, StringComparison.Ordinal);
        Assert.Contains("background: #fafafa", gateStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("swiper-bundle", host, StringComparison.Ordinal);
        Assert.DoesNotContain("init-swiper", host, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileStartup_RestoresLoginAndFirstRunResourcesOnAndroidAndIos()
    {
        var startup = Read("Watermark.Razor/Workspace/WMApplicationStartupService.cs");
        var registrations = Read("Watermark.Razor/Workspace/WMApplicationServiceCollectionExtensions.cs");
        var mobileClient = Read("Watermark.Andorid/Models/ClientInstance.cs");

        Assert.Contains("if (!Global.IsMobile)", startup, StringComparison.Ordinal);
        Assert.Contains("await accounts.RefreshAsync()", startup, StringComparison.Ordinal);
        Assert.Contains("await membership.ReconcilePendingAsync()", startup, StringComparison.Ordinal);
        Assert.Contains("B735DFC73A0B4080B11BBCFD3AE833D6", startup, StringComparison.Ordinal);
        Assert.Contains("await api.Download(DefaultTemplateId, string.Empty)", startup, StringComparison.Ordinal);
        Assert.Contains("await api.DownloadLogoes()", startup, StringComparison.Ordinal);
        Assert.Contains("android-default-resources.v1", startup, StringComparison.Ordinal);
        Assert.Contains("ios-default-resources.v1", startup, StringComparison.Ordinal);
        Assert.Contains("将在下次启动时重试", startup, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IWMApplicationStartupService, WMApplicationStartupService>()", registrations, StringComparison.Ordinal);
        Assert.Contains("#elif IOS", mobileClient, StringComparison.Ordinal);
        Assert.Contains("IdentifierForVendor?.AsString()", mobileClient, StringComparison.Ordinal);
        Assert.Contains("Preferences.Default.Set(preferenceKey, persistedId)", mobileClient, StringComparison.Ordinal);
    }

    [Fact]
    public void TransientNotifications_UseOneBottomCenteredToastContractOnMobileAndDesktop()
    {
        var common = Read("Watermark.Razor/Common.cs");
        var mobileRegistration = Read("Watermark.Andorid/MauiProgram.cs");
        var desktopRegistration = Read("Watermark.Win/Models/IocHelper.cs");
        var mobileSettings = Read("Watermark.Razor/BlazorPages/WMSettingsPage.razor");
        var desktopSettings = Read("Watermark.Razor/Components/Desktop/WMDesktopSettingsPage.razor");
        var toastStyles = Read("Watermark.Razor/wwwroot/css/wm-toast.css");
        var mobileHost = Read("Watermark.Andorid/wwwroot/index.html");
        var desktopHost = Read("Watermark.Win/wwwroot/index.html");
        var guidance = Read("AGENTS.md");

        Assert.Contains("public static void ShowToast", common, StringComparison.Ordinal);
        Assert.Contains("new SnackbarOptions(normalizedMessage)", common, StringComparison.Ordinal);
        Assert.Contains("AlertTypes.Error", common, StringComparison.Ordinal);
        Assert.Contains("SnackPosition.BottomCenter", mobileRegistration, StringComparison.Ordinal);
        Assert.Contains("SnackPosition.BottomCenter", desktopRegistration, StringComparison.Ordinal);
        Assert.Contains("nameof(PEnqueuedSnackbars.MaxCount), 1", mobileRegistration, StringComparison.Ordinal);
        Assert.Contains("nameof(PEnqueuedSnackbars.MaxCount), 1", desktopRegistration, StringComparison.Ordinal);
        Assert.Contains("Common.ShowToast(Popup", mobileSettings, StringComparison.Ordinal);
        Assert.Contains("Common.ShowToast(Popup", desktopSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("settings-message", mobileSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("settings-message", desktopSettings, StringComparison.Ordinal);
        Assert.Contains("background: #263447", toastStyles, StringComparison.Ordinal);
        Assert.Contains("border-radius: 7px", toastStyles, StringComparison.Ordinal);
        Assert.Contains("font-size: 11px", toastStyles, StringComparison.Ordinal);
        Assert.Contains("min-width: 0", toastStyles, StringComparison.Ordinal);
        Assert.Contains("padding: 9px 13px", toastStyles, StringComparison.Ordinal);
        Assert.Contains("_content/Watermark.Razor/css/wm-toast.css", mobileHost, StringComparison.Ordinal);
        Assert.Contains("_content/Watermark.Razor/css/wm-toast.css", desktopHost, StringComparison.Ordinal);
        Assert.Contains("Common.ShowToast(IPopupService, ...)", guidance, StringComparison.Ordinal);
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }
        return count;
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Watermark.sln"))) return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root.");
    }
}
