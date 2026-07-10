#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac.Editor;

public sealed class MacTemplateStore
{
    public Task SaveAsync(WMCanvas canvas)
        => SaveAsync(canvas, Global.AppPath.TemplatesFolder);

    public async Task SaveAsync(WMCanvas canvas, string templatesRoot)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatesRoot);
        EnsureSafeTemplateId(canvas.ID);

        var root = Path.GetFullPath(templatesRoot);
        var directory = Path.Combine(root, canvas.ID);
        Directory.CreateDirectory(directory);
        var errors = MacTemplateValidator.Validate(canvas, directory);
        var blocking = errors.Where(error => error.Severity == MacValidationSeverity.Error).ToList();
        if (blocking.Count > 0)
            throw new MacTemplateValidationException(blocking);

        var target = Path.Combine(directory, "config.json");
        var temporary = target + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, Global.CanvasSerialize(canvas));
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void EnsureSafeTemplateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || Path.IsPathRooted(id)
            || id.Contains('/') || id.Contains('\\') || Path.GetFileName(id) != id)
            throw new ArgumentException("模板 ID 无效。", nameof(id));
    }
}
