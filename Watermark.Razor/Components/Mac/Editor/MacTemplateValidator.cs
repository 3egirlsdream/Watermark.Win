#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac.Editor;

public enum MacValidationSeverity
{
    Warning,
    Error
}

public sealed record MacTemplateValidationError(
    string? ControlId,
    string Field,
    string Message,
    MacValidationSeverity Severity = MacValidationSeverity.Error);

public sealed class MacTemplateValidationException : Exception
{
    public MacTemplateValidationException(IReadOnlyList<MacTemplateValidationError> errors)
        : base(errors.FirstOrDefault()?.Message ?? "模板配置无效")
    {
        Errors = errors;
    }

    public IReadOnlyList<MacTemplateValidationError> Errors { get; }
}

public static class MacTemplateValidator
{
    private const double MaximumOffsetPercent = 100;
    private const double MaximumScale = 10;

    public static IReadOnlyList<MacTemplateValidationError> Validate(WMCanvas canvas, string templateDirectory)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateDirectory);

        var errors = new List<MacTemplateValidationError>();
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
        List<MacTemplateValidationError> errors)
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
                if (depth > MacControlTree.MaxContainerDepth)
                    errors.Add(new(control.ID, "Hierarchy", $"容器层级不能超过 {MacControlTree.MaxContainerDepth} 层。"));
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

    private static void ValidateTransform(IWMControl control, List<MacTemplateValidationError> errors)
    {
        var transform = control.Transform;
        if (transform == null) return;

        ValidateFiniteRange(control.ID, "Transform.OffsetXPercent", transform.OffsetXPercent, -MaximumOffsetPercent, MaximumOffsetPercent, errors);
        ValidateFiniteRange(control.ID, "Transform.OffsetYPercent", transform.OffsetYPercent, -MaximumOffsetPercent, MaximumOffsetPercent, errors);
        ValidateFiniteRange(control.ID, "Transform.ScaleX", transform.ScaleX, double.Epsilon, MaximumScale, errors);
        ValidateFiniteRange(control.ID, "Transform.ScaleY", transform.ScaleY, double.Epsilon, MaximumScale, errors);
        ValidateFiniteRange(control.ID, "Transform.Rotation", transform.Rotation, -180, 180, errors);
    }

    private static void ValidateFiniteRange(string controlId, string field, double value, double minimum, double maximum, List<MacTemplateValidationError> errors)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            errors.Add(new(controlId, field, $"{field} 必须介于 {minimum} 和 {maximum} 之间。"));
    }

    private static void ValidateImagePath(string controlId, string field, string path, string templateDirectory, bool optional, List<MacTemplateValidationError> errors)
    {
        if (TryResolveTemplateResource(templateDirectory, path, out var resolved) && File.Exists(resolved)) return;
        var severity = optional ? MacValidationSeverity.Warning : MacValidationSeverity.Error;
        errors.Add(new(controlId, field, optional ? "可选图片资源不存在。" : "必需图片资源不存在或路径无效。", severity));
    }

    private static void ValidateFontPath(WMText text, string templateDirectory, List<MacTemplateValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(text.FontFamily) || text.FontFamily == Global.DefaultFont) return;
        if (Path.GetExtension(text.FontFamily).Length == 0 && !text.FontFamily.Contains(Path.DirectorySeparatorChar) && !text.FontFamily.Contains(Path.AltDirectorySeparatorChar)) return;
        if (TryResolveTemplateResource(templateDirectory, text.FontFamily, out var resolved) && File.Exists(resolved)) return;
        errors.Add(new(text.ID, "FontFamily", "字体资源不存在或路径无效。", MacValidationSeverity.Warning));
    }

    private static bool TryResolveTemplateResource(string templateDirectory, string resourcePath, out string resolved)
    {
        resolved = string.Empty;
        if (Path.IsPathRooted(resourcePath)) return false;

        var root = Path.GetFullPath(templateDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, resourcePath));
        var relative = Path.GetRelativePath(root, candidate);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return false;

        resolved = candidate;
        return true;
    }
}
