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

    public static IReadOnlyList<WMTemplateValidationError> Validate(WMCanvas canvas, string templateDirectory)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateDirectory);

        var errors = new List<WMTemplateValidationError>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        var active = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        foreach (var root in canvas.Children ?? [])
            ValidateControl(root, 1, templateDirectory, ids, visited, active, errors);

        if (!string.IsNullOrWhiteSpace(canvas.Path))
            ValidateImagePath(canvas.ID, "Path", canvas.Path, templateDirectory, false, errors);

        return errors;
    }

    private static void ValidateControl(
        IWMControl control,
        int depth,
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
            errors.Add(new(control.ID, "ID", "控件 ID 必须唯一。"));

        ValidateTransform(control, errors);
        switch (control)
        {
            case WMContainer container:
                if (depth > WMControlTree.MaxContainerDepth)
                    errors.Add(new(control.ID, "Hierarchy", $"容器层级不能超过 {WMControlTree.MaxContainerDepth} 层。"));
                if (!string.IsNullOrWhiteSpace(container.Path))
                    ValidateImagePath(control.ID, "Path", container.Path, templateDirectory, false, errors);
                foreach (var child in container.Controls ?? [])
                    ValidateControl(child, depth + (child is WMContainer ? 1 : 0), templateDirectory, ids, visited, active, errors);
                break;
            case WMLogo logo when !string.IsNullOrWhiteSpace(logo.Path):
                ValidateImagePath(control.ID, "Path", logo.Path, templateDirectory, true, errors);
                break;
            case WMText text:
                ValidateFontPath(text, templateDirectory, errors);
                break;
        }

        active.Remove(control);
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

    private static void ValidateFiniteRange(string controlId, string field, double value, double minimum, double maximum, List<WMTemplateValidationError> errors, bool maximumInclusive = true)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum || (!maximumInclusive && value == maximum))
            errors.Add(new(controlId, field, $"{field} 必须介于 {minimum} 和 {maximum} 之间。"));
    }

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

