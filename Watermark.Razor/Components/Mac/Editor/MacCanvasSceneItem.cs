#nullable enable

using Watermark.Shared.Enums;
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

public static class MacCanvasBoundary
{
    private const double MinimumScale = 0.05;
    private const double MaximumScale = 20;

    public static MacCanvasInteraction ConstrainTransform(WMDesignBounds bounds, MacCanvasInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(interaction);
        if (!string.Equals(bounds.ControlId, interaction.ControlId, StringComparison.Ordinal))
            throw new ArgumentException("交互目标与边界不匹配。", nameof(interaction));
        if (bounds.ParentId is null || bounds.ParentWidth <= 0 || bounds.ParentHeight <= 0)
            return interaction;

        var fallbackTransform = bounds.Transform ?? new WMTransform();
        var scaleX = Math.Clamp(Math.Abs(FiniteOr(interaction.ScaleX, fallbackTransform.ScaleX)), MinimumScale, MaximumScale);
        var scaleY = Math.Clamp(Math.Abs(FiniteOr(interaction.ScaleY, fallbackTransform.ScaleY)), MinimumScale, MaximumScale);
        var rotation = FiniteOr(interaction.Rotation, fallbackTransform.Rotation);
        (scaleX, scaleY) = ClampScales(bounds, scaleX, scaleY, rotation);

        var (offsetXPercent, offsetYPercent) = ClampOffsets(
            bounds,
            interaction.OffsetXPercent,
            interaction.OffsetYPercent,
            scaleX,
            scaleY,
            rotation);
        return interaction with
        {
            OffsetXPercent = offsetXPercent,
            OffsetYPercent = offsetYPercent,
            ScaleX = scaleX,
            ScaleY = scaleY,
            Rotation = rotation
        };
    }

    public static MacCanvasInteraction ConstrainDrag(WMDesignBounds bounds, MacCanvasInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(interaction);
        if (!string.Equals(bounds.ControlId, interaction.ControlId, StringComparison.Ordinal))
            throw new ArgumentException("交互目标与边界不匹配。", nameof(interaction));
        if (!string.Equals(interaction.Kind, "drag", StringComparison.Ordinal)
            || bounds.ParentId is null
            || bounds.ParentWidth <= 0
            || bounds.ParentHeight <= 0)
            return interaction;

        return ConstrainTransform(bounds, interaction);
    }

    public static (double ScaleX, double ScaleY) ClampScales(
        WMDesignBounds bounds,
        double scaleX,
        double scaleY,
        double rotation)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (bounds.ParentId is null || bounds.ParentWidth <= 0 || bounds.ParentHeight <= 0)
            return (scaleX, scaleY);

        var fallbackTransform = bounds.Transform ?? new WMTransform();
        scaleX = Math.Clamp(Math.Abs(FiniteOr(scaleX, fallbackTransform.ScaleX)), MinimumScale, MaximumScale);
        scaleY = Math.Clamp(Math.Abs(FiniteOr(scaleY, fallbackTransform.ScaleY)), MinimumScale, MaximumScale);
        var radians = FiniteOr(rotation, fallbackTransform.Rotation) * Math.PI / 180d;
        var cosine = Math.Abs(Math.Cos(radians));
        var sine = Math.Abs(Math.Sin(radians));
        var rotatedWidth = cosine * bounds.Width * scaleX + sine * bounds.Height * scaleY;
        var rotatedHeight = sine * bounds.Width * scaleX + cosine * bounds.Height * scaleY;
        if (rotatedWidth <= 0 || rotatedHeight <= 0)
            return (scaleX, scaleY);

        var fit = Math.Min(
            1d,
            Math.Min(bounds.ParentWidth / rotatedWidth, bounds.ParentHeight / rotatedHeight));
        if (!double.IsFinite(fit) || fit <= 0)
            return (scaleX, scaleY);

        return (
            Math.Max(MinimumScale, scaleX * fit),
            Math.Max(MinimumScale, scaleY * fit));
    }

    public static (double OffsetXPercent, double OffsetYPercent) ClampOffsets(
        WMDesignBounds bounds,
        double offsetXPercent,
        double offsetYPercent,
        double scaleX,
        double scaleY,
        double rotation)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (bounds.ParentId is null || bounds.ParentWidth <= 0 || bounds.ParentHeight <= 0)
            return (offsetXPercent, offsetYPercent);

        var fallbackTransform = bounds.Transform ?? new WMTransform();
        offsetXPercent = FiniteOr(offsetXPercent, fallbackTransform.OffsetXPercent);
        offsetYPercent = FiniteOr(offsetYPercent, fallbackTransform.OffsetYPercent);
        scaleX = Math.Abs(FiniteOr(scaleX, fallbackTransform.ScaleX));
        scaleY = Math.Abs(FiniteOr(scaleY, fallbackTransform.ScaleY));
        rotation = FiniteOr(rotation, fallbackTransform.Rotation) * Math.PI / 180d;

        var cosine = Math.Abs(Math.Cos(rotation));
        var sine = Math.Abs(Math.Sin(rotation));
        var halfWidth = (cosine * bounds.Width * scaleX + sine * bounds.Height * scaleY) / 2d;
        var halfHeight = (sine * bounds.Width * scaleX + cosine * bounds.Height * scaleY) / 2d;
        var baseCenterX = bounds.X + bounds.Width / 2d;
        var baseCenterY = bounds.Y + bounds.Height / 2d;
        var desiredCenterX = baseCenterX + bounds.ParentWidth * offsetXPercent / 100d;
        var desiredCenterY = baseCenterY + bounds.ParentHeight * offsetYPercent / 100d;
        var centerX = ClampCenter(desiredCenterX, halfWidth, bounds.ParentWidth);
        var centerY = ClampCenter(desiredCenterY, halfHeight, bounds.ParentHeight);

        return (
            (centerX - baseCenterX) / bounds.ParentWidth * 100d,
            (centerY - baseCenterY) / bounds.ParentHeight * 100d);
    }

    private static double ClampCenter(double center, double halfExtent, double parentExtent)
    {
        if (!double.IsFinite(center) || !double.IsFinite(halfExtent) || parentExtent <= 0)
            return parentExtent / 2d;
        return halfExtent * 2d >= parentExtent
            ? parentExtent / 2d
            : Math.Clamp(center, halfExtent, parentExtent - halfExtent);
    }

    private static double FiniteOr(double value, double fallback) => double.IsFinite(value) ? value : fallback;
}

public static class MacCanvasFlowLayout
{
    public static int GetDropIndex(
        WMContainer parent,
        IWMControl control,
        WMDesignBounds draggedBounds,
        MacCanvasInteraction interaction,
        IReadOnlyList<WMDesignBounds> bounds)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(draggedBounds);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(bounds);

        var originalIndex = parent.Controls.IndexOf(control);
        if (originalIndex < 0) return 0;

        var byId = bounds.ToDictionary(item => item.ControlId, StringComparer.Ordinal);
        var horizontal = parent.Orientation == Orientation.Horizontal;
        var parentExtent = horizontal ? draggedBounds.ParentWidth : draggedBounds.ParentHeight;
        var offsetPercent = horizontal ? interaction.OffsetXPercent : interaction.OffsetYPercent;
        var dropCenter = (horizontal ? draggedBounds.X + draggedBounds.Width / 2d : draggedBounds.Y + draggedBounds.Height / 2d)
            + parentExtent * offsetPercent / 100d;
        if (!double.IsFinite(dropCenter)) return originalIndex;

        var index = 0;
        foreach (var sibling in parent.Controls)
        {
            if (ReferenceEquals(sibling, control)) continue;
            if (!byId.TryGetValue(sibling.ID, out var siblingBounds))
                return originalIndex;

            var transform = siblingBounds.Transform ?? new WMTransform();
            var siblingCenter = horizontal
                ? siblingBounds.X + siblingBounds.Width / 2d + siblingBounds.ParentWidth * transform.OffsetXPercent / 100d
                : siblingBounds.Y + siblingBounds.Height / 2d + siblingBounds.ParentHeight * transform.OffsetYPercent / 100d;
            if (dropCenter < siblingCenter) return index;
            index++;
        }

        return index;
    }
}

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
