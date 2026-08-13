using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWindowsDesktopParityTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void WpfHost_UsesSharedDesktopRoutesAndFullWorkspaceServiceGraph()
    {
        var root = Read("Watermark.Win/BlazorPages/MainView.razor");
        var registrations = Read("Watermark.Win/Views/MainWindow.xaml.cs");
        var auxiliary = Read("Watermark.Razor/BlazorPages/WMDesktopAuxiliaryPage.razor");

        Assert.Contains("AdditionalAssemblies", root, StringComparison.Ordinal);
        Assert.Contains("typeof(WMRootRedirect).Assembly", root, StringComparison.Ordinal);
        Assert.Contains("DeviceType.Win", registrations, StringComparison.Ordinal);
        Assert.Contains("new WMLocalSourceStager(copyLocalSources: true)", registrations, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IWMPhotoPicker, WMWpfPhotoPicker>()", registrations, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IWMExportSink, WMLocalExportSink>()", registrations, StringComparison.Ordinal);
        Assert.Contains("AddWMApplicationServices()", registrations, StringComparison.Ordinal);
        Assert.Contains("IWMHighPrecisionTemplateRenderer", registrations, StringComparison.Ordinal);
        Assert.Contains("IWMMultiFramePreviewEngine", registrations, StringComparison.Ordinal);
        Assert.Contains("WMStarTrailOperationProcessor", registrations, StringComparison.Ordinal);
        Assert.Contains("<WMNewPosterTemplateDialog", auxiliary, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMDesktopNewTemplateDialog", auxiliary, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsClient_ImplementsMacDesktopNativeContractsWithoutRuntimeStubs()
    {
        var client = Read("Watermark.Win/Models/ClientInstance.cs");

        Assert.DoesNotContain("NotImplementedException", client, StringComparison.Ordinal);
        Assert.Contains("IWindowService windows", client, StringComparison.Ordinal);
        Assert.Contains("OpenFileDialog", client, StringComparison.Ordinal);
        Assert.Contains("OpenFolderDialog", client, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetText", client, StringComparison.Ordinal);
        Assert.Contains("GetWMDesignFunc", client, StringComparison.Ordinal);
        Assert.Contains("CopyTemplateAsset", client, StringComparison.Ordinal);
        Assert.Contains("ImportFontAsync", client, StringComparison.Ordinal);
        Assert.Contains("ReLogin", client, StringComparison.Ordinal);
        Assert.Contains("WindowsUpdateChannel = \"WatermarkV3\"", client, StringComparison.Ordinal);
        Assert.Contains("OpenExternalUrlAsync(uri.AbsoluteUri)", client, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsAutomaticUpdate_RemainsOnWindowsChannelAndReturnsToDesktopSettings()
    {
        var window = Read("Watermark.Win/Views/MainWindow.xaml.cs");
        var dialog = Read("Watermark.Win/Views/UpdateWin.xaml.cs");
        var dialogXaml = Read("Watermark.Win/Views/UpdateWin.xaml");

        Assert.Contains("Loaded += MainWindow_Loaded", window, StringComparison.Ordinal);
        Assert.Contains("day % 3", window, StringComparison.Ordinal);
        Assert.Contains("client.CheckUpdate(\"WatermarkV3\")", window, StringComparison.Ordinal);
        Assert.Contains("new UpdateWin", window, StringComparison.Ordinal);
        Assert.Contains("/desktop/settings?returnUrl=%2Fdesktop", dialog, StringComparison.Ordinal);
        Assert.Contains("Title=\"软件更新\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("OK_Click", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("NextTime_Click", dialogXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsDesktop_DeclaresNativeArchitecturesAndMacEquivalentSizing()
    {
        var project = Read("Watermark.Win/Watermark.Win.csproj");
        var hostPage = Read("Watermark.Win/wwwroot/index.html");
        var window = Read("Watermark.Win/Views/MainWindow.xaml");
        var registrations = Read("Watermark.Win/Views/MainWindow.xaml.cs");
        var nativeLoader = Read("Watermark.Win/Models/WMWindowsNativeLibraryLoader.cs");
        var slider = Read("Watermark.Razor/Parts/SliderInput.razor");
        var color = Read("Watermark.Razor/Parts/ColorPicker.razor");

        Assert.Contains("native\\artifacts\\win-x64\\Watermark.Imaging.Native.dll", project, StringComparison.Ordinal);
        Assert.Contains("native\\artifacts\\win-arm64\\Watermark.Imaging.Native.dll", project, StringComparison.Ordinal);
        Assert.Contains("runtimes\\win-x64\\native\\Watermark.Imaging.Native.dll", project, StringComparison.Ordinal);
        Assert.Contains("runtimes\\win-arm64\\native\\Watermark.Imaging.Native.dll", project, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.ProcessArchitecture", nativeLoader, StringComparison.Ordinal);
        Assert.Contains("Architecture.X64", nativeLoader, StringComparison.Ordinal);
        Assert.Contains("Architecture.Arm64", nativeLoader, StringComparison.Ordinal);
        Assert.Contains("WMWindowsNativeLibraryLoader.Register()", registrations, StringComparison.Ordinal);
        Assert.Contains("AppContext.BaseDirectory", nativeLoader, StringComparison.Ordinal);
        Assert.Contains("NativeLibrary.SetDllImportResolver", nativeLoader, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>app.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("Icon=\"pack://application:,,,/app.ico\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"app.ico\"", window, StringComparison.Ordinal);
        var icon = File.ReadAllBytes(Path.Combine(RepositoryRoot, "Watermark.Win", "app.ico"));
        Assert.Equal(0, icon[0]);
        Assert.Equal(0, icon[1]);
        Assert.Equal(1, BitConverter.ToUInt16(icon, 2));
        Assert.True(BitConverter.ToUInt16(icon, 4) >= 8, "Windows 应用图标必须包含完整的多尺寸帧。");
        Assert.Contains("MinHeight=\"600\"", window, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"900\"", window, StringComparison.Ordinal);
        Assert.Contains("_content/Watermark.Razor/Watermark.Razor.bundle.scp.css", hostPage, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"Watermark.Win.styles.css\"", hostPage, StringComparison.Ordinal);
        Assert.Contains("DeviceType.Mac or Shared.Enums.DeviceType.Win", slider, StringComparison.Ordinal);
        Assert.Contains("DeviceType.Mac or Watermark.Shared.Enums.DeviceType.Win", color, StringComparison.Ordinal);
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
