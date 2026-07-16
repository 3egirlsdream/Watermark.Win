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
        Assert.Contains("await InitializeApplicationSafelyAsync();", gate, StringComparison.Ordinal);
        Assert.Equal(1, Count(gate, "StartupService.InitializeAfterPrivacyConsentAsync()"));
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
