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

    [Fact]
    public void InspectorSlider_ProvidesTheMobileWheelPickerAndVerticalNudgeContract()
    {
        var componentPath = Path.Combine(
            FindRepositoryRoot(),
            "Watermark.Razor",
            "Components",
            "Mac",
            "MacInspectorSlider.razor");
        var cssPath = Path.Combine(
            FindRepositoryRoot(),
            "Watermark.Razor",
            "Components",
            "Mac",
            "MacInspectorSlider.razor.css");
        var component = File.ReadAllText(componentPath);
        var css = File.ReadAllText(cssPath);

        Assert.Contains("class=\"mobile-number-trigger\"", component, StringComparison.Ordinal);
        Assert.Contains("aria-haspopup=\"listbox\"", component, StringComparison.Ordinal);
        Assert.Contains("@onpointermove=\"MoveNumberGestureAsync\"", component, StringComparison.Ordinal);
        Assert.Contains("@onwheel=\"AdjustPickerWheelAsync\"", component, StringComparison.Ordinal);
        Assert.Contains("PickerValues", component, StringComparison.Ordinal);
        Assert.Contains("BeginMobileInteractionAsync", component, StringComparison.Ordinal);
        Assert.Contains("EndMobileInteractionAsync", component, StringComparison.Ordinal);
        Assert.Contains(".mobile-number-picker", css, StringComparison.Ordinal);
        Assert.Contains("top: 50%", css, StringComparison.Ordinal);
        Assert.Contains("touch-action: none", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-areas: \"label value\"", css, StringComparison.Ordinal);
        Assert.Contains(".inspector-slider-range {\n        display: none;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorSlider_ElevatesAnOpenPickerAboveTheMobilePanel()
    {
        var root = FindRepositoryRoot();
        var slider = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "Components", "Mac", "MacInspectorSlider.razor"));
        var sliderCss = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "Components", "Mac", "MacInspectorSlider.razor.css"));
        var inspectorCss = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "Components", "Mac", "MacSelectionInspector.razor.css"));
        var designerCss = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "Workspace", "Components", "WMTemplateDesigner.razor.css"));
        var script = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "wwwroot", "js", "mac-inspector-slider.js"));

        Assert.Contains("syncMobilePickerLayer", slider, StringComparison.Ordinal);
        Assert.Contains("syncMobilePickerLayer", script, StringComparison.Ordinal);
        Assert.Contains("mobile-number-picker-open", script, StringComparison.Ordinal);
        Assert.Contains("z-index: 2147483000", sliderCss, StringComparison.Ordinal);
        Assert.Contains("mobile-number-picker-open", inspectorCss, StringComparison.Ordinal);
        Assert.Contains("mobile-number-picker-open", designerCss, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileNumericSlider_IsTheDocumentedPublicControlForMobileParameterInputs()
    {
        var root = FindRepositoryRoot();
        var component = File.ReadAllText(Path.Combine(
            root, "Watermark.Razor", "Components", "Compatibility", "WmNumericSlider.razor"));
        var guide = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var script = File.ReadAllText(Path.Combine(
            root, "Watermark.Razor", "wwwroot", "js", "mac-inspector-slider.js"));
        var mobileInputs = new[]
        {
            "Watermark.Razor/Workspace/Components/WMTemplateBorderControl.razor",
            "Watermark.Razor/Workspace/Components/WMColorParameterControl.razor",
            "Watermark.Razor/Workspace/Components/WMCropControls.razor",
            "Watermark.Razor/Workspace/Components/WMExportPanel.razor",
            "Watermark.Razor/BlazorPages/WMSettingsPage.razor",
            "Watermark.Razor/Parts/SliderInput.razor",
            "Watermark.Razor/Parts/ColorPicker.razor"
        };

        Assert.Contains("<MacInspectorSlider", component, StringComparison.Ordinal);
        Assert.Contains("InteractionStarted", component, StringComparison.Ordinal);
        Assert.Contains("InteractionEnded", component, StringComparison.Ordinal);
        Assert.Contains("不得新增原生 `input[type=\"range\"]`", guide, StringComparison.Ordinal);
        Assert.Contains(".wm-mobile-dock", script, StringComparison.Ordinal);
        Assert.Contains(".workspace-export-drawer", script, StringComparison.Ordinal);

        foreach (var input in mobileInputs)
        {
            var source = File.ReadAllText(Path.Combine(root, input));
            Assert.Contains("WmNumericSlider", source, StringComparison.Ordinal);
            Assert.DoesNotContain("type=\"range\"", source, StringComparison.Ordinal);
        }
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
