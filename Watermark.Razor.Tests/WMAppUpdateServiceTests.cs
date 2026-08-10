using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMAppUpdateServiceTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task StartUpdate_PublishesDownloadProgressUntilInstallerLaunch()
    {
        var client = new UpdateClient();
        var service = new WMAppUpdateService(client);
        var states = new List<WMUpdateState>();
        service.Changed += () => states.Add(service.State);

        var checkedState = await service.CheckAsync();
        var result = await service.StartUpdateAsync();

        Assert.True(checkedState.HasChecked);
        Assert.True(checkedState.UpdateAvailable);
        Assert.Equal("更新内容", checkedState.ReleaseNotes);
        Assert.True(result.Succeeded);
        Assert.True(client.UpdateCalled);
        Assert.Contains(states, state => state.IsDownloading && state.DownloadProgress == 25);
        Assert.Contains(states, state => state.IsDownloading && state.DownloadProgress == 75);
        Assert.False(service.State.IsDownloading);
        Assert.Equal(100, service.State.DownloadProgress);
        Assert.False(service.State.HasError);
    }

    [Fact]
    public async Task CheckUpdate_ReportsServiceFailureInsteadOfLatestVersion()
    {
        var client = new UpdateClient { CheckFailure = new InvalidOperationException("网络不可用") };
        var service = new WMAppUpdateService(client);

        var state = await service.CheckAsync();

        Assert.True(state.HasChecked);
        Assert.True(state.HasError);
        Assert.False(state.UpdateAvailable);
        Assert.Contains("网络不可用", state.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("最新版本", state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidInstaller_UsesAnInternalUpdatesProviderAndAsyncStreamingDownload()
    {
        var manifest = Read("Watermark.Andorid/Platforms/Android/AndroidManifest.xml");
        var providerPaths = Read("Watermark.Andorid/Platforms/Android/Resources/xml/update_provider_paths.xml");
        var upgradeService = Read("Watermark.Andorid/Models/UpgradeService.cs");

        Assert.Contains("android.permission.REQUEST_INSTALL_PACKAGES", manifest, StringComparison.Ordinal);
        Assert.Contains("${applicationId}.updateFileProvider", manifest, StringComparison.Ordinal);
        Assert.Contains("@xml/update_provider_paths", manifest, StringComparison.Ordinal);
        Assert.Contains("<files-path name=\"updates\" path=\"updates/\"", providerPaths, StringComparison.Ordinal);
        Assert.Contains("$\"{AppInfo.PackageName}.updateFileProvider\"", upgradeService, StringComparison.Ordinal);
        Assert.Contains("HttpCompletionOption.ResponseHeadersRead", upgradeService, StringComparison.Ordinal);
        Assert.Contains("response.EnsureSuccessStatusCode()", upgradeService, StringComparison.Ordinal);
        Assert.DoesNotContain(".Result", upgradeService, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Watermark.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private sealed class UpdateClient : IClientInstance
    {
        public Exception? CheckFailure { get; init; }
        public bool UpdateCalled { get; private set; }
        public string UpdateMessage { get; set; } = "更新内容";
        public string UpdateVersion { get; set; } = "2.0.0";
        public string LinkPath { get; set; } = "https://example.com/update.apk";
        public string AppTitle { get; set; } = string.Empty;

        public string Key() => "test";
        public void Haptic() { }
        public Task<IEnumerable<string>> PickMultipleAsync() => Task.FromResult<IEnumerable<string>>([]);
        public Task<string> PickAsync() => Task.FromResult(string.Empty);
        public Version GetVersion() => new(1, 0, 0);
        public Task<bool> CheckUpdate(string platform = "WatermarkAndroid") =>
            CheckFailure is null ? Task.FromResult(true) : Task.FromException<bool>(CheckFailure);
        public Task SetTextAsync(string uri) => Task.CompletedTask;
        public Task OpenExternalUrlAsync(string url) => Task.CompletedTask;
        public Task<API<string>> AliPays(decimal cost, string tradeName) =>
            Task.FromResult(new API<string> { success = false });
        public Task ReLogin() => Task.CompletedTask;
        public Task<bool> IsOutOfDate(string client = "Watermark_A") => Task.FromResult(false);
        public bool Save(byte[] b64, string fn) => false;
        public Task<string> OpenFolder() => Task.FromResult(string.Empty);
        public void SetColor(string color = "#F5F5F5") { }
        public Task<WMDesignFunc> GetWMDesignFunc(string canvasId) =>
            Task.FromException<WMDesignFunc>(new NotSupportedException());

        public Task Update(Action<long, long> downloadProgressChanged)
        {
            UpdateCalled = true;
            downloadProgressChanged(25, 100);
            downloadProgressChanged(75, 100);
            downloadProgressChanged(100, 100);
            return Task.CompletedTask;
        }

        public Task<bool> Download(string directory, string fileName, string extension) => Task.FromResult(false);
        public void Exit() { }
        public void OpenDesign(WMCanvas canvas) { }
        public Task InteropInit(string appId) => Task.CompletedTask;
        public void WindowMinimize() { }
        public void WindowZoom() { }
        public void WindowClose() { }
        public void WindowStartDrag() { }
        public void WindowDragMove() { }
        public void WindowEndDrag() { }
    }
}
