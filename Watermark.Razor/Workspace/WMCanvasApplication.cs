#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>Builds an image-specific runtime canvas without mutating the stored template.</summary>
public static class WMCanvasApplication
{
    public static WMCanvas CreateForImage(WMCanvas source, WMTemplateList image)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(image);

        var canvas = Global.ReadConfig(Global.CanvasSerialize(source));
        canvas.Path = !string.IsNullOrWhiteSpace(image.Canvas?.Path)
            ? image.Canvas.Path
            : image.Path ?? string.Empty;
        canvas.Exif = CloneExif(image.Canvas?.Exif ?? source.Exif);
        return canvas;
    }

    private static Dictionary<string, Dictionary<string, string>> CloneExif(
        IReadOnlyDictionary<string, Dictionary<string, string>>? exif) =>
        exif?.ToDictionary(
            pair => pair.Key,
            pair => new Dictionary<string, string>(pair.Value)) ?? [];
}
