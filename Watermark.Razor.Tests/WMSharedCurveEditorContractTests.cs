using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMSharedCurveEditorContractTests
{
    [Fact]
    public void Desktop_ReusesTheMobileCurveEditorForEveryColorChannel()
    {
        var desktop = Read("Watermark.Razor", "Components", "Desktop", "WMDesktopModeInspector.razor");

        Assert.Contains("<WMMobileMasterCurve", desktop, StringComparison.Ordinal);
        Assert.Contains("ShowPresets=\"false\"", desktop, StringComparison.Ordinal);
        Assert.Contains("Channel=\"@ChannelCss(SelectedCurveChannel)\"", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("<WMDesktopCurveEditor", desktop, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Watermark.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Unable to find the Watermark repository root.");
    }
}
