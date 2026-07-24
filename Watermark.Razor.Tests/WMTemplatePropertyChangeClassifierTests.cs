using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMTemplatePropertyChangeClassifierTests
{
    [Theory]
    [InlineData("修改字体")]
    [InlineData("配置拍摄信息")]
    [InlineData("修改关联容器")]
    [InlineData("切换自动品牌色")]
    [InlineData("清除图标")]
    public void ResourceProperties_InvalidateResourceLayoutAndPaint(string label)
    {
        var kind = WMTemplatePropertyChangeClassifier.Classify(false, label);

        Assert.True(kind.HasFlag(WMTemplateChangeKind.Resource));
        Assert.True(kind.HasFlag(WMTemplateChangeKind.Layout));
        Assert.True(kind.HasFlag(WMTemplateChangeKind.Paint));
    }

    [Theory]
    [InlineData("修改字号")]
    [InlineData("修改字距")]
    [InlineData("切换文字换行")]
    [InlineData("切换粗体")]
    [InlineData("切换斜体")]
    [InlineData("切换文字边框")]
    [InlineData("修改分割线方向")]
    public void IntrinsicSizeProperties_InvalidateLayoutAndPaint(string label)
    {
        var kind = WMTemplatePropertyChangeClassifier.Classify(false, label);

        Assert.False(kind.HasFlag(WMTemplateChangeKind.Resource));
        Assert.True(kind.HasFlag(WMTemplateChangeKind.Layout));
        Assert.True(kind.HasFlag(WMTemplateChangeKind.Paint));
    }

    [Fact]
    public void BackdropAndCanvasChanges_HaveDedicatedInvalidation()
    {
        Assert.Equal(
            WMTemplateChangeKind.Backdrop | WMTemplateChangeKind.Paint,
            WMTemplatePropertyChangeClassifier.Classify(false, "修改模糊强度"));
        Assert.Equal(
            WMTemplateChangeKind.Canvas | WMTemplateChangeKind.Backdrop,
            WMTemplatePropertyChangeClassifier.Classify(true, "切换画布模糊"));
        Assert.Equal(
            WMTemplateChangeKind.Canvas,
            WMTemplatePropertyChangeClassifier.Classify(true, "修改画布背景"));
    }
}
