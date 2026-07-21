using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacInspectorLocalizationTests
{
    [Fact]
    public void TemplateInspector_DoesNotExposeCssPropertyNamesOrEnglishOptionValues()
    {
        var root = FindRepositoryRoot();
        var styleInspector = File.ReadAllText(Path.Combine(
            root, "Watermark.Razor", "Components", "Mac", "MacNodeStyleInspector.razor"));
        var selectionInspector = File.ReadAllText(Path.Combine(
            root, "Watermark.Razor", "Components", "Mac", "MacSelectionInspector.razor"));

        foreach (var text in new[]
                 {
                     "Label=\"position\"", "Label=\"width\"", "Label=\"height\"",
                     "Label=\"flex\"", "Label=\"overflow\"", "Label=\"gap\"",
                     "Label=\"margin-top\"", "Label=\"padding\"",
                     ">Flex 容器<", ">box model", ">inset",
                     "\"static\"", "\"absolute\"", "\"initial\"",
                     "\"hidden\"", "\"visible\"", "\"row\"", "\"column\"",
                     "\"baseline\""
                 })
        {
            Assert.DoesNotContain(text, styleInspector, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Unit=\"px\"", selectionInspector, StringComparison.Ordinal);
        Assert.DoesNotContain("（px）", selectionInspector, StringComparison.Ordinal);
        Assert.DoesNotContain(">EXIF 配置<", selectionInspector, StringComparison.Ordinal);
        Assert.DoesNotContain("<h3>Logo</h3>", selectionInspector, StringComparison.Ordinal);
        Assert.Contains("Label=\"定位方式\"", styleInspector, StringComparison.Ordinal);
        Assert.Contains("Label=\"主轴对齐\"", styleInspector, StringComparison.Ordinal);
        Assert.Contains("拍摄信息配置", selectionInspector, StringComparison.Ordinal);
        Assert.Contains("Label=\"字距\"", selectionInspector, StringComparison.Ordinal);
        Assert.Contains("Label=\"背景模糊\"", selectionInspector, StringComparison.Ordinal);
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
