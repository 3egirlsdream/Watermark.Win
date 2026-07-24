using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Central declaration of property invalidation semantics. Keeping this
/// outside the visual component makes the mapping executable in regression
/// tests instead of relying on UI labels being reviewed by hand.
/// </summary>
public static class WMTemplatePropertyChangeClassifier
{
    public static WMTemplateChangeKind Classify(
        bool targetsCanvas,
        string propertyLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyLabel);
        if (targetsCanvas)
        {
            return propertyLabel.Contains("模糊", StringComparison.Ordinal)
                ? WMTemplateChangeKind.Canvas | WMTemplateChangeKind.Backdrop
                : WMTemplateChangeKind.Canvas;
        }

        if (propertyLabel.Contains("背景模糊", StringComparison.Ordinal)
            || propertyLabel.Contains("模糊强度", StringComparison.Ordinal))
            return WMTemplateChangeKind.Backdrop | WMTemplateChangeKind.Paint;

        if (ContainsAny(
                propertyLabel,
                "图片",
                "图标",
                "字体",
                "拍摄信息",
                "关联容器",
                "自动品牌"))
        {
            return WMTemplateChangeKind.Resource
                | WMTemplateChangeKind.Layout
                | WMTemplateChangeKind.Paint;
        }

        if (ContainsAny(
                propertyLabel,
                "字号",
                "字距",
                "换行",
                "粗体",
                "斜体",
                "文字边框",
                "宽度",
                "高度",
                "尺寸",
                "长度",
                "外边距",
                "方向",
                "对齐"))
        {
            return WMTemplateChangeKind.Layout
                | WMTemplateChangeKind.Paint;
        }

        return WMTemplateChangeKind.Paint;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate =>
            value.Contains(candidate, StringComparison.Ordinal));
}
