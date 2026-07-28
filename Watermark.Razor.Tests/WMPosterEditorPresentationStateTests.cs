using Watermark.Razor.Components.Compatibility;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMPosterEditorPresentationStateTests
{
    [Fact]
    public void Panels_UseTheDeclaredFixedSpaceInsteadOfAContinuousHeight()
    {
        var state = new WMPosterEditorPresentationState();

        Assert.Equal(WMPosterMobilePanel.None, state.Panel);
        Assert.False(state.HasOpenPanel);

        state.Open(WMPosterMobilePanel.Add);
        Assert.True(state.HasOpenPanel);
        Assert.Equal(WMMobileEditorSpace.Small, state.Space);
        Assert.Equal("mobile-space-small", state.SpaceCssClass);

        state.Open(WMPosterMobilePanel.Properties);
        Assert.Equal(WMMobileEditorSpace.Medium, state.Space);

        state.Open(WMPosterMobilePanel.AssetCrop);
        Assert.Equal(WMMobileEditorSpace.Medium, state.Space);

        state.Open(WMPosterMobilePanel.Layers);
        Assert.Equal(WMMobileEditorSpace.Large, state.Space);
        Assert.Equal("mobile-space-large", state.SpaceCssClass);
    }

    [Fact]
    public void TextTools_ExposeContentComposerAndGroupedInspectorTabs()
    {
        var tools = WMPosterEditorPresentationState.ToolsFor(new WMText());

        var content = Assert.Single(tools, tool => tool.Id == "text-content");
        Assert.Equal(WMPosterMobilePanel.TextContent, content.Panel);
        Assert.Equal(WMMobileEditorSpace.Medium, content.Space);
        Assert.Equal(WMMobileEditorSpace.Large,
            Assert.Single(tools, tool => tool.Id == "text-typography").Space);
        Assert.Equal(WMTemplateMobileInspectorSection.TextTypography,
            Assert.Single(tools, tool => tool.Id == "text-typography").InspectorSection);
        Assert.Equal(WMTemplateMobileInspectorSection.TextBorder,
            Assert.Single(tools, tool => tool.Id == "text-border").InspectorSection);
        Assert.Contains(tools, tool => tool.Id == "size");
        Assert.Contains(tools, tool => tool.Id == "position" && tool.Space == WMMobileEditorSpace.Medium);
        Assert.Contains(tools, tool => tool.Id == "spacing" && tool.Space == WMMobileEditorSpace.Large);
        Assert.DoesNotContain(tools, tool => tool.Id.StartsWith("margin-", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Id == "overflow");
        Assert.Equal("layers", tools[^2].Id);
        Assert.Equal("add", tools[^1].Id);
    }

    [Fact]
    public void CanvasTools_GroupInsetsAndEffectsIntoCompactOperationPanels()
    {
        var tools = WMPosterEditorPresentationState.ToolsFor(null);

        Assert.Contains(tools, tool => tool.Id == "canvas-size" && tool.Space == WMMobileEditorSpace.Medium);
        Assert.Contains(tools, tool => tool.Id == "canvas-insets" && tool.Space == WMMobileEditorSpace.Medium);
        Assert.DoesNotContain(tools, tool => tool.Id.StartsWith("canvas-inset-", StringComparison.Ordinal));
        Assert.Contains(tools, tool => tool.Id == "canvas-frame" && tool.Space == WMMobileEditorSpace.Large);
        Assert.Contains(tools, tool => tool.Id == "canvas-image" && tool.Space == WMMobileEditorSpace.Large);
        Assert.DoesNotContain(tools, tool => tool.Id.StartsWith("canvas-frame-", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Id.StartsWith("canvas-image-", StringComparison.Ordinal));
    }

    [Fact]
    public void ExifFieldLibrary_PromotesToLargeAndBackClosesOneLevelAtATime()
    {
        var state = new WMPosterEditorPresentationState();
        state.Open(WMPosterMobilePanel.TextContent);

        state.SetTextFieldLibraryOpen(true);
        Assert.True(state.IsTextFieldLibraryOpen);
        Assert.Equal(WMMobileEditorSpace.Large, state.Space);

        Assert.True(state.TryClosePanel());
        Assert.False(state.IsTextFieldLibraryOpen);
        Assert.Equal(WMPosterMobilePanel.TextContent, state.Panel);
        Assert.Equal(WMMobileEditorSpace.Medium, state.Space);

        Assert.True(state.TryClosePanel());
        Assert.Equal(WMPosterMobilePanel.None, state.Panel);
        Assert.False(state.TryClosePanel());
    }

    [Fact]
    public void ImageAndContainerTools_OpenTheUnifiedAssetLibraryAtLargeSpace()
    {
        var imageTools = WMPosterEditorPresentationState.ToolsFor(new WMLogo());
        var containerTools = WMPosterEditorPresentationState.ToolsFor(new WMContainer());

        var replace = Assert.Single(imageTools, tool => tool.Id == "replace");
        Assert.Equal(WMPosterMobilePanel.AssetLibrary, replace.Panel);
        Assert.Equal(WMMobileEditorSpace.Large, replace.Space);
        Assert.Equal(WMTemplateMobileInspectorSection.Logo,
            Assert.Single(imageTools, tool => tool.Id == "logo").InspectorSection);
        Assert.Equal(WMMobileEditorSpace.Medium,
            Assert.Single(imageTools, tool => tool.Id == "logo-sizing").Space);
        Assert.Contains(imageTools, tool => tool.Id == "position");
        Assert.Equal("layers", imageTools[^2].Id);
        Assert.Equal("add", imageTools[^1].Id);
        Assert.Equal("replace-background", containerTools[0].Id);
        Assert.Equal(WMPosterMobilePanel.AssetLibrary, containerTools[0].Panel);
        Assert.Equal(WMMobileEditorSpace.Large, containerTools[0].Space);
        Assert.Contains(containerTools, tool => tool.Id == "spacing" && tool.Space == WMMobileEditorSpace.Large);
        Assert.Contains(containerTools, tool => tool.Id == "container-image" && tool.Space == WMMobileEditorSpace.Large);
        Assert.DoesNotContain(containerTools, tool => tool.Id == "padding");
        Assert.DoesNotContain(containerTools, tool => tool.Id == "overflow");
        Assert.Contains(containerTools, tool => tool.Id == "flow" && tool.Space == WMMobileEditorSpace.Large);
    }

    [Fact]
    public void InspectorTabKeepsItsOwnActiveIdentityWhileRelatedPanelsKeepTheSelection()
    {
        var state = new WMPosterEditorPresentationState();
        var typography = WMPosterEditorPresentationState.ToolsFor(new WMText())
            .Single(tool => tool.Id == "text-typography");

        state.Open(typography);

        Assert.Equal("text-typography", state.ActiveToolId);
        Assert.Equal(WMTemplateMobileInspectorSection.TextTypography, state.InspectorSection);
        Assert.Equal(WMPosterMobilePanel.Properties, state.Panel);

        state.OpenRelatedPanel(WMPosterMobilePanel.AssetCrop, WMMobileEditorSpace.Medium);
        Assert.Equal("text-typography", state.ActiveToolId);

        state.ClosePanel();
        Assert.Null(state.ActiveToolId);
        Assert.Null(state.InspectorSection);
    }

    [Fact]
    public void ContextToolIcons_AllResolveFromTheBundledPhosphorSubset()
    {
        IWMControl?[] selections =
        [
            null,
            new WMLogo(),
            new WMContainer(),
            new WMLine(),
            new WMText()
        ];

        var icons = selections
            .SelectMany(WMPosterEditorPresentationState.ToolsFor)
            .Select(tool => tool.Icon)
            .Distinct(StringComparer.Ordinal);

        Assert.All(
            icons,
            icon => Assert.False(string.IsNullOrWhiteSpace(WmPhosphorIconPaths.Get(icon))));
    }
}
