#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac.Editor;

public sealed record MacCanvasSceneItem(
    string Id,
    string? ParentId,
    string Type,
    double X,
    double Y,
    double Width,
    double Height,
    double ParentWidth,
    double ParentHeight,
    double OffsetXPercent,
    double OffsetYPercent,
    double ScaleX,
    double ScaleY,
    double Rotation,
    bool Locked,
    bool Visible);

public sealed record MacCanvasInteraction(
    string ControlId,
    string Kind,
    double OffsetXPercent,
    double OffsetYPercent,
    double ScaleX,
    double ScaleY,
    double Rotation);

public static class MacCanvasTransform
{
    public static void Apply(IWMControl control, MacCanvasInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(interaction);
        if (!string.Equals(control.ID, interaction.ControlId, StringComparison.Ordinal))
            throw new ArgumentException("交互目标与控件不匹配。", nameof(interaction));

        var transform = control.EnsureTransform();
        transform.OffsetXPercent = ClampFinite(interaction.OffsetXPercent, transform.OffsetXPercent, -500, 500);
        transform.OffsetYPercent = ClampFinite(interaction.OffsetYPercent, transform.OffsetYPercent, -500, 500);
        transform.ScaleX = ClampFinite(interaction.ScaleX, transform.ScaleX, 0.05, 20);
        transform.ScaleY = ClampFinite(interaction.ScaleY, transform.ScaleY, 0.05, 20);
        transform.Rotation = double.IsFinite(interaction.Rotation)
            ? interaction.Rotation
            : transform.Rotation;
        if (control is WMContainer container)
            container.Angle = (int)Math.Round(transform.Rotation);
    }

    private static double ClampFinite(double value, double fallback, double minimum, double maximum) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, minimum, maximum);
}
