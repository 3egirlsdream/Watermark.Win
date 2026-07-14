using Watermark.Razor.Components.Mac;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacSliderTests
{
    [Fact]
    public void NormalizeStep_RemovesFloatConversionNoise()
    {
        var convertedFloat = (double)0.05f;

        Assert.Equal(0.05d, MacSlider.NormalizeStep(convertedFloat));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0d)]
    [InlineData(-0.1d)]
    public void NormalizeStep_UsesSafeDefaultForInvalidValues(double step)
    {
        Assert.Equal(1d, MacSlider.NormalizeStep(step));
    }

    [Fact]
    public void MacSlider_DoesNotDependOnMasaSliderJavascriptInterop()
    {
        var componentPath = Path.Combine(
            FindRepositoryRoot(),
            "Watermark.Razor",
            "Components",
            "Mac",
            "MacSlider.razor");
        var source = File.ReadAllText(componentPath);

        Assert.Contains("type=\"range\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<MSlider", source, StringComparison.Ordinal);
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
