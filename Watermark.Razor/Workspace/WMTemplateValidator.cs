#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public enum WMValidationSeverity
{
    Warning,
    Error
}

public sealed record WMTemplateValidationError(
    string? ControlId,
    string Field,
    string Message,
    WMValidationSeverity Severity = WMValidationSeverity.Error);

public sealed class WMTemplateValidationException : Exception
{
    public WMTemplateValidationException(IReadOnlyList<WMTemplateValidationError> errors)
        : base(errors.FirstOrDefault()?.Message ?? "模板配置无效")
    {
        Errors = errors;
    }

    public IReadOnlyList<WMTemplateValidationError> Errors { get; }
}

public static class WMTemplateValidator
{
    private const double MinimumOffsetPercent = -500;
    private const double MaximumOffsetPercent = 500;
    private const double MinimumScale = 0.05;
    private const double MaximumScale = 20;
    private static readonly string[] PhotoMetadataNameHints =
    [
        "机型", "镜头", "相机", "曝光", "拍摄", "时间", "日期", "坐标", "位置", "地点",
        "编号", "期号", "画幅", "胶卷", "参数", "光圈", "快门", "焦距", "感光", "ISO"
    ];

    public static IReadOnlyList<WMTemplateValidationError> Validate(WMCanvas canvas, string templateDirectory)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateDirectory);

        var errors = new List<WMTemplateValidationError>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        var active = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        foreach (var root in canvas.Children ?? [])
            ValidateControl(root, 1, canvas.LayoutSchemaVersion >= WMLayoutMigration.CurrentSchemaVersion, true, templateDirectory, ids, visited, active, errors);

        if (!string.IsNullOrWhiteSpace(canvas.Path))
            ValidateImagePath(canvas.ID, "Path", canvas.Path, templateDirectory, false, errors);

        return errors;
    }

    private static void ValidateControl(
        IWMControl control,
        int depth,
        bool v2,
        bool isRoot,
        string templateDirectory,
        HashSet<string> ids,
        HashSet<IWMControl> visited,
        HashSet<IWMControl> active,
        List<WMTemplateValidationError> errors)
    {
        if (!active.Add(control))
        {
            errors.Add(new(control.ID, "Hierarchy", "控件层级存在循环。"));
            return;
        }

        if (!visited.Add(control))
        {
            errors.Add(new(control.ID, "Hierarchy", "同一控件不能出现在多个位置。"));
            active.Remove(control);
            return;
        }

        if (string.IsNullOrWhiteSpace(control.ID) || !ids.Add(control.ID))
            errors.Add(new(control.ID, "ID", "控件标识必须唯一。"));

        ValidateTransform(control, errors);
        if (v2) ValidateStyle(control, isRoot, errors);
        switch (control)
        {
            case WMContainer container:
                if (depth > WMControlTree.MaxContainerDepth)
                    errors.Add(new(control.ID, "Hierarchy", $"容器层级不能超过 {WMControlTree.MaxContainerDepth} 层。"));
                ValidateContainerEffects(container, v2, errors);
                if (!string.IsNullOrWhiteSpace(container.Path))
                    ValidateImagePath(control.ID, "Path", container.Path, templateDirectory, false, errors);
                foreach (var child in container.Controls ?? [])
                    ValidateControl(child, depth + (child is WMContainer ? 1 : 0), v2, false, templateDirectory, ids, visited, active, errors);
                break;
            case WMLogo logo when !string.IsNullOrWhiteSpace(logo.Path):
                ValidateImagePath(control.ID, "Path", logo.Path, templateDirectory, true, errors);
                break;
            case WMText text:
                ValidateFontPath(text, templateDirectory, errors);
                if (v2)
                {
                    ValidateFiniteRange(text.ID, "LetterSpacing", text.LetterSpacing, -1, 3, errors);
                    ValidatePhotoMetadataBinding(text, errors);
                }
                break;
        }

        active.Remove(control);
    }

    private static void ValidatePhotoMetadataBinding(WMText text, List<WMTemplateValidationError> errors)
    {
        if (!PhotoMetadataNameHints.Any(hint => (text.Name ?? string.Empty).Contains(hint, StringComparison.Ordinal)))
            return;

        var entries = text.Exifs ?? [];
        if (entries.Count == 0 || entries.All(entry => string.IsNullOrWhiteSpace(entry.Key)))
        {
            errors.Add(new(
                text.ID,
                "Exifs",
                "照片信息文字必须绑定相机元数据字段，示例值不能写成固定文字。",
                WMValidationSeverity.Warning));
        }
    }

    private static void ValidateTransform(IWMControl control, List<WMTemplateValidationError> errors)
    {
        var transform = control.Transform;
        if (transform == null) return;

        ValidateFiniteRange(control.ID, "Transform.OffsetXPercent", transform.OffsetXPercent, MinimumOffsetPercent, MaximumOffsetPercent, errors);
        ValidateFiniteRange(control.ID, "Transform.OffsetYPercent", transform.OffsetYPercent, MinimumOffsetPercent, MaximumOffsetPercent, errors);
        ValidateFiniteRange(control.ID, "Transform.ScaleX", transform.ScaleX, MinimumScale, MaximumScale, errors);
        ValidateFiniteRange(control.ID, "Transform.ScaleY", transform.ScaleY, MinimumScale, MaximumScale, errors);
        ValidateFiniteRange(control.ID, "Transform.Rotation", transform.Rotation, -180, 180, errors, maximumInclusive: false);
    }

    private static void ValidateContainerEffects(WMContainer container, bool v2, List<WMTemplateValidationError> errors)
    {
        if (!v2 || !container.ContainerProperties.EnableGaussianBlur) return;

        ValidateFiniteRange(container.ID, "ContainerProperties.GaussianDeep", container.ContainerProperties.GaussianDeep, 1, 60, errors);
        var background = container.BackgroundColor;
        var opaque = !string.IsNullOrWhiteSpace(background)
            && (background.Length == 7 || background.EndsWith("FF", StringComparison.OrdinalIgnoreCase));
        if (opaque)
        {
            errors.Add(new(
                container.ID,
                "BackgroundColor",
                "背景模糊容器使用了不透明背景色，模糊结果会被填充遮住。",
                WMValidationSeverity.Warning));
        }
    }

    private static void ValidateStyle(IWMControl control, bool isRoot, List<WMTemplateValidationError> errors)
    {
        var style = control.Style;
        if (isRoot && style.Position != WMPosition.Absolute)
            errors.Add(new(control.ID, "position", "根容器必须使用自由定位。"));
        ValidateLength(control.ID, "width", style.Width, 0, 100, errors);
        ValidateLength(control.ID, "height", style.Height, 0, 100, errors);
        ValidateInsets(control.ID, style, errors);
        ValidateThickness(control.ID, "margin", style.Margin, -25, 25, errors);
        ValidateThickness(control.ID, "padding", style.Padding, 0, 25, errors);
        ValidateFiniteRange(control.ID, "gap", style.Gap, 0, 25, errors);
        if (style.Position == WMPosition.Absolute)
        {
            ValidateFiniteRange(control.ID, "transform.scaleX", style.Transform.ScaleX, 0.1, 4, errors);
            ValidateFiniteRange(control.ID, "transform.scaleY", style.Transform.ScaleY, 0.1, 4, errors);
            ValidateFiniteRange(control.ID, "transform.rotation", style.Transform.Rotation, -180, 180, errors, maximumInclusive: false);
        }
        else if (style.Transform.OffsetXPercent != 0 || style.Transform.OffsetYPercent != 0
            || style.Transform.ScaleX != 1 || style.Transform.ScaleY != 1 || style.Transform.Rotation != 0)
        {
            errors.Add(new(control.ID, "transform", "流式布局节点不能使用变换；请改用外边距或自由定位边距。"));
        }
    }

    private static void ValidateInsets(string controlId, WMStyle style, List<WMTemplateValidationError> errors)
    {
        ValidateOptionalLength(controlId, "top", style.Top, -25, 125, errors);
        ValidateOptionalLength(controlId, "right", style.Right, -25, 125, errors);
        ValidateOptionalLength(controlId, "bottom", style.Bottom, -25, 125, errors);
        ValidateOptionalLength(controlId, "left", style.Left, -25, 125, errors);
    }

    private static void ValidateThickness(string controlId, string field, WMThickness thickness, double minimum, double maximum, List<WMTemplateValidationError> errors)
    {
        ValidateFiniteRange(controlId, $"{field}.top", thickness.Top, minimum, maximum, errors);
        ValidateFiniteRange(controlId, $"{field}.right", thickness.Right, minimum, maximum, errors);
        ValidateFiniteRange(controlId, $"{field}.bottom", thickness.Bottom, minimum, maximum, errors);
        ValidateFiniteRange(controlId, $"{field}.left", thickness.Left, minimum, maximum, errors);
    }

    private static void ValidateOptionalLength(string controlId, string field, WMStyleLength? value, double minimum, double maximum, List<WMTemplateValidationError> errors)
    {
        if (value is not null) ValidateLength(controlId, field, value, minimum, maximum, errors);
    }

    private static void ValidateLength(string controlId, string field, WMStyleLength value, double minimum, double maximum, List<WMTemplateValidationError> errors)
    {
        if (!value.IsAuto) ValidateFiniteRange(controlId, field, value.Value, minimum, maximum, errors);
    }

    private static void ValidateFiniteRange(string controlId, string field, double value, double minimum, double maximum, List<WMTemplateValidationError> errors, bool maximumInclusive = true)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum || (!maximumInclusive && value == maximum))
            errors.Add(new(controlId, field, $"{DisplayField(field)}必须介于 {minimum} 和 {maximum} 之间。"));
    }

    private static string DisplayField(string field) => field switch
    {
        "position" => "定位方式",
        "width" => "宽度",
        "height" => "高度",
        "top" => "上定位边距",
        "right" => "右定位边距",
        "bottom" => "下定位边距",
        "left" => "左定位边距",
        "margin.top" => "上外边距",
        "margin.right" => "右外边距",
        "margin.bottom" => "下外边距",
        "margin.left" => "左外边距",
        "padding.top" => "上内边距",
        "padding.right" => "右内边距",
        "padding.bottom" => "下内边距",
        "padding.left" => "左内边距",
        "gap" => "统一间距",
        "Exifs" => "拍摄信息配置",
        "LetterSpacing" => "字距",
        "ContainerProperties.GaussianDeep" => "背景模糊强度",
        "transform.scaleX" or "Transform.ScaleX" => "水平缩放",
        "transform.scaleY" or "Transform.ScaleY" => "垂直缩放",
        "transform.rotation" or "Transform.Rotation" => "旋转角度",
        "Transform.OffsetXPercent" => "水平坐标",
        "Transform.OffsetYPercent" => "垂直坐标",
        _ => "配置值"
    };

    private static void ValidateImagePath(string controlId, string field, string path, string templateDirectory, bool optional, List<WMTemplateValidationError> errors)
    {
        if (TryResolveTemplateResource(templateDirectory, path, out var resolved) && File.Exists(resolved)) return;
        var severity = optional ? WMValidationSeverity.Warning : WMValidationSeverity.Error;
        errors.Add(new(controlId, field, optional ? "可选图片资源不存在。" : "必需图片资源不存在或路径无效。", severity));
    }

    private static void ValidateFontPath(WMText text, string templateDirectory, List<WMTemplateValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(text.FontFamily) || text.FontFamily == Global.DefaultFont) return;
        if (Path.GetExtension(text.FontFamily).Length == 0 && !text.FontFamily.Contains(Path.DirectorySeparatorChar) && !text.FontFamily.Contains(Path.AltDirectorySeparatorChar)) return;
        if (TryResolveTemplateResource(templateDirectory, text.FontFamily, out var resolved) && File.Exists(resolved)) return;
        errors.Add(new(text.ID, "FontFamily", "字体资源不存在或路径无效。", WMValidationSeverity.Warning));
    }

    private static bool TryResolveTemplateResource(string templateDirectory, string resourcePath, out string resolved)
    {
        resolved = string.Empty;
        if (Path.IsPathRooted(resourcePath))
        {
            resolved = Path.GetFullPath(resourcePath);
            return true;
        }

        var root = Path.GetFullPath(templateDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, resourcePath));
        var relative = Path.GetRelativePath(root, candidate);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return false;

        resolved = candidate;
        return true;
    }
}
