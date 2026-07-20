using System.Xml.Linq;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMImagingRolloutBuildContractTests
{
    [Fact]
    public void AndroidHost_ForwardsEveryImagingRolloutPropertyToRazor()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "Watermark.Andorid", "Watermark.Andorid.csproj"));
        var reference = document.Descendants("ProjectReference")
            .Single(item => string.Equals(
                (string?)item.Attribute("Include"),
                "..\\Watermark.Razor\\Watermark.Razor.csproj",
                StringComparison.Ordinal));
        var forwarded = ((string?)reference.Element("AdditionalProperties"))
            ?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.Split('=', 2)[0])
            .ToHashSet(StringComparer.Ordinal)
            ?? [];

        var expected = new[]
        {
            "WMImagingMasterEnabled",
            "WMImagingRawEnabled",
            "WMImagingStarTrailEnabled",
            "WMImagingMultiFrameEnabled",
            "WMImagingPng16Enabled",
            "WMImagingTiff16Enabled",
            "WMImagingAllowQaOverride"
        };
        Assert.Equal(
            expected.OrderBy(value => value, StringComparer.Ordinal),
            forwarded.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Watermark.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Unable to find the Watermark repository root.");
    }
}
