using SkiaSharp;
using Watermark.Shared.Enums;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMTextContentComposerTests
{
    [Fact]
    public void SharedRazor_DoesNotUseLegacyValueSelectors()
    {
        var root = FindRepositoryRoot();
        var razorRoot = Path.Combine(root, "Watermark.Razor");
        var offenders = Directory
            .EnumerateFiles(razorRoot, "*.razor", SearchOption.AllDirectories)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("<select", StringComparison.OrdinalIgnoreCase)
                       || source.Contains("<MSelect", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Editor_UsesBoundedFieldScrollerAndSharedWmSelect()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(
            root, "Watermark.Razor", "Components", "ExifConfig.razor"));
        var styles = File.ReadAllText(Path.Combine(
            root, "Watermark.Razor", "Components", "ExifConfig.razor.css"));
        var compactStyleEditor = File.ReadAllText(Path.Combine(
            root, "Watermark.Razor", "Components", "CompactFontStyleEditor.razor"));
        var wmSelect = File.ReadAllText(Path.Combine(
            root, "Watermark.Razor", "Components", "Compatibility", "WmSelect.razor"));
        var contributorGuide = File.ReadAllText(Path.Combine(root, "AGENTS.md"));

        Assert.Contains("class=\"field-actions\"", markup, StringComparison.Ordinal);
        Assert.Contains("<CompactFontStyleEditor", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<FontStyleComp", markup, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 auto;", styles, StringComparison.Ordinal);
        Assert.Contains("flex: 0 0 auto;", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("<select", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<select", compactStyleEditor, StringComparison.Ordinal);
        Assert.Contains("<WmSelect", markup, StringComparison.Ordinal);
        Assert.Contains("<WmSelect", compactStyleEditor, StringComparison.Ordinal);
        Assert.Contains("open = !open", wmSelect, StringComparison.Ordinal);
        Assert.Contains("open = false", wmSelect, StringComparison.Ordinal);
        Assert.Contains("下拉选择时统一使用公共组件", contributorGuide, StringComparison.Ordinal);
        Assert.Contains("WmSelectOption<TValue>", contributorGuide, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_MixesLiteralMetadataAndExplicitLineBreak()
    {
        var entries = new[]
        {
            new WMExifConfigInfo { Prefix = "SHOT ON", SeparatorAfter = " " },
            new WMExifConfigInfo { Key = "Make", SeparatorAfter = " " },
            new WMExifConfigInfo { Key = "Model", SeparatorAfter = "\n" },
            new WMExifConfigInfo
            {
                Key = "DateTimeOriginal",
                DateTimeFormat = "yyyy.MM.dd",
                SeparatorAfter = string.Empty
            }
        };

        var result = WMTextContentPreview.Resolve(entries);

        Assert.Equal("SHOT ON FUJIFILM X-T5\n2024.04.04", result);
    }

    [Fact]
    public void Preview_UsesFallbackOnlyWhenRuntimeMetadataIsMissing()
    {
        var entry = new WMExifConfigInfo
        {
            Key = "Model",
            Value = "X-T5",
            Prefix = "[",
            Suffix = "]",
            EmptyValueFallback = "—"
        };

        var result = WMTextContentPreview.Resolve(
            [entry],
            new Dictionary<string, string>());

        Assert.Equal("[—]", result);
    }

    [Fact]
    public void Preview_LegacyNullSeparatorStillUsesSingleSpace()
    {
        var result = WMTextContentPreview.Resolve(
        [
            new WMExifConfigInfo { Prefix = "A" },
            new WMExifConfigInfo { Prefix = "B" }
        ]);

        Assert.Equal("A B", result);
    }

    [Fact]
    public void Renderer_RespectsExplicitSeparator()
    {
        var canvas = new WMCanvas
        {
            LayoutSchemaVersion = WMLayoutMigration.CurrentSchemaVersion,
            ImageProperties = new WMImage { Show = true }
        };
        var text = new WMText
        {
            FontSize = 5,
            Exifs =
            [
                new WMExifConfigInfo { Prefix = "A", SeparatorAfter = string.Empty },
                new WMExifConfigInfo { Prefix = "B", SeparatorAfter = string.Empty }
            ]
        };
        var root = new WMContainer
        {
            BackgroundColor = "#00000000",
            Style = new WMStyle
            {
                Position = WMPosition.Absolute,
                Width = WMStyleLength.Percent(80),
                Height = WMStyleLength.Percent(30),
                Left = WMStyleLength.Percent(10),
                Top = WMStyleLength.Percent(10),
                FlexDirection = Orientation.Horizontal,
                JustifyContent = WMJustifyContent.Start,
                AlignItems = WMAlignItems.Center
            },
            Controls = [text]
        };
        canvas.Children.Add(root);
        canvas.Exif[canvas.ID] = new Dictionary<string, string>();

        using var source = new SKBitmap(1080, 720, SKColorType.Bgra8888, SKAlphaType.Premul);
        source.Erase(SKColors.White);
        var helper = new WatermarkHelper();

        using var compact = helper.RenderSrgbSurfaceWithLayout(canvas, source).Bitmap;
        var compactWidth = text.Width;

        text.Exifs[0].SeparatorAfter = " ";
        using var spaced = helper.RenderSrgbSurfaceWithLayout(canvas, source).Bitmap;

        Assert.True(text.Width > compactWidth);
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
