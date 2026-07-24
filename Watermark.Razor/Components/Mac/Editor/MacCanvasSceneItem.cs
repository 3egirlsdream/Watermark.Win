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
    bool Absolute,
    bool Locked,
    bool Visible,
    bool HasSurface,
    bool Backdrop,
    string ResizeMode,
    double ResizeInset,
    double ResizeFontSize,
    long BoundsVersion);

public sealed record MacCanvasInteraction(
    string ControlId,
    string Kind,
    double OffsetXPercent,
    double OffsetYPercent,
    double ScaleX,
    double ScaleY,
    double Rotation,
    double Width = 0,
    double Height = 0,
    string? Handle = null,
    double CenterDeltaX = 0,
    double CenterDeltaY = 0,
    long BoundsVersion = 0,
    bool KeepAspectRatio = true,
    double ResizeRatio = 0);

public static class MacCanvasBoundary
{
    private const double MinimumScale = 0.1;
    private const double MaximumScale = 4;

    public static MacCanvasInteraction ConstrainTransform(WMDesignBounds bounds, MacCanvasInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(interaction);
        if (!string.Equals(bounds.ControlId, interaction.ControlId, StringComparison.Ordinal))
            throw new ArgumentException("交互目标与边界不匹配。", nameof(interaction));
        if (bounds.ParentWidth <= 0 || bounds.ParentHeight <= 0)
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
        if (bounds.ParentWidth <= 0 || bounds.ParentHeight <= 0)
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
        var centerX = bounds.ParentId is null
            ? ClampRootCenter(desiredCenterX, halfWidth, bounds.ParentWidth)
            : ClampCenter(desiredCenterX, halfWidth, bounds.ParentWidth);
        var centerY = bounds.ParentId is null
            ? ClampRootCenter(desiredCenterY, halfHeight, bounds.ParentHeight)
            : ClampCenter(desiredCenterY, halfHeight, bounds.ParentHeight);

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

    private static double ClampRootCenter(double center, double halfExtent, double parentExtent)
    {
        if (!double.IsFinite(center) || !double.IsFinite(halfExtent) || parentExtent <= 0)
            return parentExtent / 2d;
        var minimumVisible = Math.Min(24d, Math.Max(0, halfExtent));
        return Math.Clamp(
            center,
            minimumVisible - halfExtent,
            parentExtent - minimumVisible + halfExtent);
    }

    private static double FiniteOr(double value, double fallback) => double.IsFinite(value) ? value : fallback;
}

public static class MacCanvasFlowLayout
{
    public sealed record DropResult(
        int Index,
        double Left,
        double Top,
        double Right,
        double Bottom);

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

    /// <summary>
    /// Resolves a flow-layout drag in two phases.  The insertion index is based
    /// on the dragged component's visual center; margins are then calculated
    /// from the component's position in that new order.  Calculating a margin
    /// from the old position would make every cross-over jump by the width or
    /// height of the sibling that was crossed.
    /// </summary>
    public static DropResult ResolveDrop(
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

        var index = GetDropIndex(parent, control, draggedBounds, interaction, bounds);
        var orderedControls = parent.Controls
            .Where(item => !ReferenceEquals(item, control))
            .ToList();
        index = Math.Clamp(index, 0, orderedControls.Count);
        orderedControls.Insert(index, control);

        var predicted = GetVisualPosition(parent, orderedControls, control, draggedBounds, bounds);
        var desiredX = draggedBounds.X + draggedBounds.ParentWidth * interaction.OffsetXPercent / 100d;
        var desiredY = draggedBounds.Y + draggedBounds.ParentHeight * interaction.OffsetYPercent / 100d;
        var deltaX = FiniteOr(desiredX - predicted.X, 0);
        var deltaY = FiniteOr(desiredY - predicted.Y, 0);

        var left = control.Margin.Left;
        var top = control.Margin.Top;
        var right = control.Margin.Right;
        var bottom = control.Margin.Bottom;
        ApplyHorizontalMarginOffset(ref left, ref right, deltaX, draggedBounds.ParentWidth);

        if (parent.Orientation == Orientation.Horizontal)
        {
            ApplyPairedMarginOffset(ref top, ref bottom, deltaY, draggedBounds.ParentHeight);
        }
        else if (parent.VerticalAlignment == VerticalAlignment.Center)
        {
            // In centered vertical flow, Top moves this component forward and
            // Bottom advances the following cursor back.  Moving both equally
            // preserves the surrounding siblings while adjusting this item.
            var minimumExtent = Math.Min(draggedBounds.ParentWidth, draggedBounds.ParentHeight);
            var amount = ToPercent(deltaY, minimumExtent);
            top += amount;
            bottom += amount;
        }
        else
        {
            ApplyPairedMarginOffset(ref top, ref bottom, deltaY, draggedBounds.ParentHeight);
        }

        return new DropResult(
            index,
            ClampMargin(left),
            ClampMargin(top),
            ClampMargin(right),
            ClampMargin(bottom));
    }

    /// <summary>
    /// V2 counterpart of <see cref="ResolveDrop"/>.  The first phase derives an
    /// insertion slot from Flex's visual order (including reverse directions).
    /// Only after that slot is known do we calculate the residual four-way
    /// margin, in canvas-short-edge percent.  This prevents a cross-over from
    /// being interpreted as a giant margin on the old sibling order.
    /// </summary>
    public static DropResult ResolveV2Drop(
        WMContainer parent,
        IWMControl control,
        WMDesignBounds draggedBounds,
        MacCanvasInteraction interaction,
        IReadOnlyList<WMDesignBounds> bounds,
        double canvasShortEdge)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(draggedBounds);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(bounds);

        var flowWithout = parent.Controls
            .Where(item => !ReferenceEquals(item, control) && item.Style.Position == WMPosition.Static)
            .ToList();
        var insertion = GetV2FlowInsertion(parent, control, draggedBounds, interaction, bounds, flowWithout);
        insertion = Math.Clamp(insertion, 0, flowWithout.Count);
        flowWithout.Insert(insertion, control);

        var predicted = GetV2VisualPosition(parent, flowWithout, control, draggedBounds, bounds, canvasShortEdge);
        var desiredX = draggedBounds.X + draggedBounds.ParentWidth * interaction.OffsetXPercent / 100d;
        var desiredY = draggedBounds.Y + draggedBounds.ParentHeight * interaction.OffsetYPercent / 100d;
        var deltaX = FiniteOr(desiredX - predicted.X, 0);
        var deltaY = FiniteOr(desiredY - predicted.Y, 0);

        var left = control.Style.Margin.Left;
        var top = control.Style.Margin.Top;
        var right = control.Style.Margin.Right;
        var bottom = control.Style.Margin.Bottom;
        ApplyV2PairedMarginOffset(ref left, ref right, deltaX, canvasShortEdge);
        ApplyV2PairedMarginOffset(ref top, ref bottom, deltaY, canvasShortEdge);

        // WMControlTree.Move uses the full Children list. Locate the next flow
        // sibling in that list so absolute decorative nodes keep their own
        // z-index placement and never become part of the visual ordering.
        var fullWithout = parent.Controls.Where(item => !ReferenceEquals(item, control)).ToList();
        var nextFlow = insertion < flowWithout.Count - 1 ? flowWithout[insertion + 1] : null;
        var fullIndex = nextFlow is null
            ? fullWithout.Count
            : fullWithout.IndexOf(nextFlow);

        return new DropResult(
            Math.Max(0, fullIndex),
            ClampV2Margin(left),
            ClampV2Margin(top),
            ClampV2Margin(right),
            ClampV2Margin(bottom));
    }

    /// <summary>
    /// Persists the result of a direct flow drag. V2 writes only Style.Margin;
    /// legacy templates keep using their legacy margin. Keeping this operation
    /// beside drop resolution makes mobile and desktop commits identical and
    /// gives the render-affecting mutation a focused test seam.
    /// </summary>
    public static bool ApplyDrop(
        WMCanvas canvas,
        WMContainer parent,
        IWMControl control,
        DropResult drop)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(drop);

        if (!Watermark.Razor.Workspace.WMControlTree.Move(canvas, control.ID, parent.ID, drop.Index))
            return false;

        var margin = canvas.LayoutSchemaVersion >= WMLayoutMigration.CurrentSchemaVersion
            ? control.Style.Margin
            : control.Margin;
        margin.Left = drop.Left;
        margin.Top = drop.Top;
        margin.Right = drop.Right;
        margin.Bottom = drop.Bottom;
        return true;
    }

    private static int GetV2FlowInsertion(
        WMContainer parent,
        IWMControl control,
        WMDesignBounds draggedBounds,
        MacCanvasInteraction interaction,
        IReadOnlyList<WMDesignBounds> bounds,
        IReadOnlyList<IWMControl> flowWithout)
    {
        var byId = bounds.ToDictionary(item => item.ControlId, StringComparer.Ordinal);
        var horizontal = parent.Style.FlexDirection == Orientation.Horizontal;
        var extent = horizontal ? draggedBounds.ParentWidth : draggedBounds.ParentHeight;
        var offset = horizontal ? interaction.OffsetXPercent : interaction.OffsetYPercent;
        var center = (horizontal ? draggedBounds.X + draggedBounds.Width / 2d : draggedBounds.Y + draggedBounds.Height / 2d)
            + extent * offset / 100d;
        if (!double.IsFinite(center)) return Math.Clamp(parent.Controls.IndexOf(control), 0, flowWithout.Count);

        var visual = parent.Style.FlexReverse ? flowWithout.Reverse().ToArray() : flowWithout.ToArray();
        for (var visualIndex = 0; visualIndex < visual.Length; visualIndex++)
        {
            if (!byId.TryGetValue(visual[visualIndex].ID, out var sibling)) continue;
            var siblingCenter = horizontal ? sibling.X + sibling.Width / 2d : sibling.Y + sibling.Height / 2d;
            if (center < siblingCenter)
                return parent.Style.FlexReverse ? flowWithout.Count - visualIndex : visualIndex;
        }

        return parent.Style.FlexReverse ? 0 : flowWithout.Count;
    }

    private static (double X, double Y) GetV2VisualPosition(
        WMContainer parent,
        IReadOnlyList<IWMControl> orderedFlow,
        IWMControl target,
        WMDesignBounds parentBounds,
        IReadOnlyList<WMDesignBounds> bounds,
        double canvasShortEdge)
    {
        var byId = bounds.ToDictionary(item => item.ControlId, StringComparer.Ordinal);
        var horizontal = parent.Style.FlexDirection == Orientation.Horizontal;
        var extent = horizontal ? parentBounds.ParentWidth : parentBounds.ParentHeight;
        var gap = Math.Max(0, parent.Style.Gap) * Math.Max(0, canvasShortEdge) / 100d;
        var unit = Math.Max(0, canvasShortEdge) / 100d;
        var items = orderedFlow.Select(item =>
        {
            byId.TryGetValue(item.ID, out var rendered);
            var size = horizontal ? rendered?.Width ?? item.Width : rendered?.Height ?? item.Height;
            var before = (horizontal ? item.Style.Margin.Left : item.Style.Margin.Top) * unit;
            var after = (horizontal ? item.Style.Margin.Right : item.Style.Margin.Bottom) * unit;
            return (Control: item, Size: size, Before: before, After: after, Rendered: rendered);
        }).ToArray();
        var used = items.Sum(item => item.Before + item.Size + item.After) + gap * Math.Max(0, items.Length - 1);
        var justify = parent.Style.JustifyContent switch
        {
            WMJustifyContent.Center => Math.Max(0, (extent - used) / 2d),
            WMJustifyContent.End => Math.Max(0, extent - used),
            _ => 0d
        };

        if (!parent.Style.FlexReverse)
        {
            var cursor = justify;
            foreach (var item in items)
            {
                cursor += item.Before;
                if (ReferenceEquals(item.Control, target))
                    return horizontal
                        ? (cursor, item.Rendered?.Y ?? parentBounds.Y)
                        : (item.Rendered?.X ?? parentBounds.X, cursor);
                cursor += item.Size + item.After + gap;
            }
        }
        else
        {
            var cursor = extent - justify;
            foreach (var item in items)
            {
                cursor -= item.After + item.Size;
                if (ReferenceEquals(item.Control, target))
                    return horizontal
                        ? (cursor, item.Rendered?.Y ?? parentBounds.Y)
                        : (item.Rendered?.X ?? parentBounds.X, cursor);
                cursor -= item.Before + gap;
            }
        }

        return (parentBounds.X, parentBounds.Y);
    }

    private static void ApplyV2PairedMarginOffset(ref double leading, ref double trailing, double offset, double canvasShortEdge)
    {
        var amount = canvasShortEdge > 0 && double.IsFinite(offset)
            ? offset / canvasShortEdge * 100d / 2d
            : 0;
        leading += amount;
        trailing -= amount;
    }

    private static double ClampV2Margin(double value) => Math.Clamp(double.IsFinite(value) ? value : 0, -25, 25);

    private static (double X, double Y) GetVisualPosition(
        WMContainer parent,
        IReadOnlyList<IWMControl> controls,
        IWMControl target,
        WMDesignBounds parentBounds,
        IReadOnlyList<WMDesignBounds> bounds)
    {
        var boundsById = bounds.ToDictionary(item => item.ControlId, StringComparer.Ordinal);
        var width = parentBounds.ParentWidth;
        var height = parentBounds.ParentHeight;
        var occupyX = 0d;
        var occupyY = 0d;

        foreach (var component in controls)
        {
            var (componentWidth, componentHeight, transform) = GetMetrics(component, boundsById);
            var x = 0d;
            var y = 0d;

            if (parent.Orientation == Orientation.Horizontal)
            {
                y = parent.VerticalAlignment switch
                {
                    VerticalAlignment.Top => 0,
                    VerticalAlignment.Bottom => height - componentHeight,
                    _ => (height - componentHeight) / 2d
                };
                y += (component.Margin.Top - component.Margin.Bottom) / 100d * height;

                if (parent.HorizontalAlignment == HorizontalAlignment.Left)
                {
                    x = occupyX + width * (component.Margin.Left - component.Margin.Right) / 100d;
                    occupyX = x + componentWidth;
                }
                else if (parent.HorizontalAlignment == HorizontalAlignment.Center)
                {
                    if (occupyX == 0)
                    {
                        // This deliberately mirrors WatermarkHelper's existing
                        // layout rule so the editor's post-drop margin matches
                        // the renderer, including old templates.
                        var totalWidth = controls.Sum(item => GetMetrics(item, boundsById).Width)
                            + controls.Sum(item => (item.Margin.Left + item.Margin.Right) / 100d * parent.HeightPercent);
                        occupyX = (width - totalWidth) / 2d;
                    }
                    x = occupyX + width * (component.Margin.Left - component.Margin.Right) / 100d;
                    occupyX = x + componentWidth;
                }
                else
                {
                    if (occupyX == 0) occupyX = width;
                    x = occupyX - componentWidth - width * (component.Margin.Right - component.Margin.Left) / 100d;
                    occupyX = x;
                }
            }
            else
            {
                x = parent.HorizontalAlignment switch
                {
                    HorizontalAlignment.Left => 0,
                    HorizontalAlignment.Right => width - componentWidth,
                    _ => (width - componentWidth) / 2d
                };
                x += (component.Margin.Left - component.Margin.Right) / 100d * width;

                if (parent.VerticalAlignment == VerticalAlignment.Top)
                {
                    y = height * (component.Margin.Top - component.Margin.Bottom) / 100d;
                    occupyY = componentHeight + y;
                }
                else if (parent.VerticalAlignment == VerticalAlignment.Center)
                {
                    var minimumExtent = Math.Min(height, width);
                    if (occupyY == 0)
                    {
                        var totalHeight = controls.Sum(item => GetMetrics(item, boundsById).Height)
                            + controls.Sum(item => (item.Margin.Top - item.Margin.Bottom) / 100d * minimumExtent);
                        occupyY = (height - totalHeight) / 2d;
                    }
                    occupyY += component.Margin.Top / 100d * minimumExtent;
                    y = occupyY;
                    occupyY = y + componentHeight - minimumExtent * component.Margin.Bottom / 100d;
                }
                else
                {
                    if (occupyY == 0) occupyY = height;
                    y = occupyY - componentHeight - height * (component.Margin.Bottom - component.Margin.Top) / 100d;
                    occupyY = y;
                }
            }

            if (ReferenceEquals(component, target))
                return (
                    x + width * transform.OffsetXPercent / 100d,
                    y + height * transform.OffsetYPercent / 100d);
        }

        return (parentBounds.X, parentBounds.Y);
    }

    private static (double Width, double Height, WMTransform Transform) GetMetrics(
        IWMControl control,
        IReadOnlyDictionary<string, WMDesignBounds> bounds)
    {
        if (bounds.TryGetValue(control.ID, out var rendered))
            return (rendered.Width, rendered.Height, rendered.Transform ?? control.Transform ?? new WMTransform());
        return (control.Width, control.Height, control.Transform ?? new WMTransform());
    }

    private static void ApplyHorizontalMarginOffset(ref double left, ref double right, double offset, double extent) =>
        ApplyPairedMarginOffset(ref left, ref right, offset, extent);

    private static void ApplyPairedMarginOffset(ref double leading, ref double trailing, double offset, double extent)
    {
        var amount = ToPercent(offset, extent) / 2d;
        leading += amount;
        trailing -= amount;
    }

    private static double ToPercent(double value, double extent) =>
        extent > 0 && double.IsFinite(value) ? value / extent * 100d : 0;

    private static double ClampMargin(double value) =>
        Math.Clamp(double.IsFinite(value) ? value : 0, -100, 100);

    private static double FiniteOr(double value, double fallback) => double.IsFinite(value) ? value : fallback;
}

public static class MacCanvasTransform
{
    private const double MinimumScale = 0.1;
    private const double MaximumScale = 4;

    public static void Apply(IWMControl control, MacCanvasInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(interaction);
        if (!string.Equals(control.ID, interaction.ControlId, StringComparison.Ordinal))
            throw new ArgumentException("交互目标与控件不匹配。", nameof(interaction));

        var transform = control.Style.Position == WMPosition.Absolute
            ? control.Style.Transform
            : control.EnsureTransform();
        transform.OffsetXPercent = ClampFinite(interaction.OffsetXPercent, transform.OffsetXPercent, -100, 100);
        transform.OffsetYPercent = ClampFinite(interaction.OffsetYPercent, transform.OffsetYPercent, -100, 100);
        transform.ScaleX = ClampFinite(interaction.ScaleX, transform.ScaleX, MinimumScale, MaximumScale);
        transform.ScaleY = ClampFinite(interaction.ScaleY, transform.ScaleY, MinimumScale, MaximumScale);
        transform.Rotation = double.IsFinite(interaction.Rotation)
            ? interaction.Rotation
            : transform.Rotation;
        if (control.Style.Position != WMPosition.Absolute && control is WMContainer container)
            container.Angle = (int)Math.Round(transform.Rotation);
    }

    private static double ClampFinite(double value, double fallback, double minimum, double maximum) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, minimum, maximum);
}

public static class MacCanvasResize
{
    private const double MinimumLayoutPixels = 0.01;
    private const double MinimumFontSize = 0.1;
    private const double MaximumFontSize = 25;

    public static MacCanvasInteraction Apply(
        IWMControl control,
        WMDesignBounds bounds,
        MacCanvasInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(interaction);
        if (!string.Equals(control.ID, interaction.ControlId, StringComparison.Ordinal)
            || !string.Equals(bounds.ControlId, interaction.ControlId, StringComparison.Ordinal))
            throw new ArgumentException("交互目标与尺寸边界不匹配。", nameof(interaction));
        if (!string.Equals(interaction.Kind, "resize", StringComparison.Ordinal))
            throw new ArgumentException("原生尺寸策略只能处理 Resize。", nameof(interaction));
        if (bounds.ParentWidth <= 0 || bounds.ParentHeight <= 0)
            throw new ArgumentException("父坐标系尺寸无效。", nameof(bounds));

        var handle = interaction.Handle ?? string.Empty;
        var changesWidth = handle.Contains('e') || handle.Contains('w');
        var changesHeight = handle.Contains('n') || handle.Contains('s');
        var requestedWidth = Math.Max(
            MinimumLayoutPixels,
            double.IsFinite(interaction.Width) ? interaction.Width : bounds.Width);
        var requestedHeight = Math.Max(
            MinimumLayoutPixels,
            double.IsFinite(interaction.Height) ? interaction.Height : bounds.Height);
        if (control is WMLine { Orientation: Orientation.Horizontal })
            requestedHeight = bounds.Height;
        else if (control is WMLine)
            requestedWidth = bounds.Width;

        switch (control)
        {
            case WMContainer:
                if (changesWidth) SetWidth(control, requestedWidth, bounds.ParentWidth);
                if (changesHeight) SetHeight(control, requestedHeight, bounds.ParentHeight);
                break;
            case WMLogo:
            {
                // Moveable already applies the corner ratio and minimum size
                // to the live proxy. Persist those exact dimensions so the
                // authoritative frame cannot jump after pointer-up.
                if (changesWidth) SetWidth(control, requestedWidth, bounds.ParentWidth);
                if (changesHeight) SetHeight(control, requestedHeight, bounds.ParentHeight);
                break;
            }
            case WMText text:
                if (changesWidth && changesHeight)
                {
                    var ratio = double.IsFinite(interaction.ResizeRatio) && interaction.ResizeRatio > 0
                        ? interaction.ResizeRatio
                        : requestedWidth / Math.Max(bounds.Width, MinimumLayoutPixels);
                    text.FontSize = Math.Clamp(
                        text.FontSize * (double.IsFinite(ratio) ? ratio : 1d),
                        MinimumFontSize,
                        MaximumFontSize);
                    if (!text.Style.Width.IsAuto)
                        SetWidth(text, requestedWidth, bounds.ParentWidth);
                }
                else if (changesWidth)
                {
                    SetWidth(text, requestedWidth, bounds.ParentWidth);
                }
                text.Style.Height = WMStyleLength.Auto();
                break;
            case WMLine line when line.Orientation == Orientation.Horizontal:
                if (changesWidth)
                    line.LengthPercent = Math.Max(MinimumLayoutPixels, requestedWidth)
                        / bounds.ParentWidth * 100d;
                break;
            case WMLine line:
                if (changesHeight)
                    line.LengthPercent = Math.Max(MinimumLayoutPixels, requestedHeight)
                        / bounds.ParentHeight * 100d;
                break;
            default:
                throw new InvalidOperationException($"不支持调整 {control.GetType().Name} 的原生尺寸。");
        }

        var appliedInteraction = control is WMLine
            ? ResolveLineAnchorInteraction(
                control,
                bounds,
                interaction,
                requestedWidth,
                requestedHeight)
            : interaction;
        if (control.Style.Position == WMPosition.Absolute)
            ApplyAnchorPosition(
                control,
                bounds,
                appliedInteraction,
                requestedWidth,
                requestedHeight);

        var appliedWidth = ResolvePixels(control.Style.Width, bounds.ParentWidth, requestedWidth);
        var appliedHeight = ResolvePixels(control.Style.Height, bounds.ParentHeight, requestedHeight);
        return appliedInteraction with
        {
            Width = appliedWidth,
            Height = appliedHeight,
            ScaleX = control.Style.Transform.ScaleX,
            ScaleY = control.Style.Transform.ScaleY,
            Rotation = control.Style.Transform.Rotation,
            OffsetXPercent = control.Style.Transform.OffsetXPercent,
            OffsetYPercent = control.Style.Transform.OffsetYPercent
        };
    }

    private static MacCanvasInteraction ResolveLineAnchorInteraction(
        IWMControl control,
        WMDesignBounds bounds,
        MacCanvasInteraction interaction,
        double requestedWidth,
        double requestedHeight)
    {
        var handle = interaction.Handle ?? string.Empty;
        var directionX = handle.Contains('e') ? 1d : handle.Contains('w') ? -1d : 0d;
        var directionY = handle.Contains('s') ? 1d : handle.Contains('n') ? -1d : 0d;
        var transform = bounds.Transform ?? control.Style.Transform;
        var localX = directionX
            * (requestedWidth - bounds.Width) / 2d
            * transform.ScaleX;
        var localY = directionY
            * (requestedHeight - bounds.Height) / 2d
            * transform.ScaleY;
        var radians = transform.Rotation * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return interaction with
        {
            CenterDeltaX = localX * cosine - localY * sine,
            CenterDeltaY = localX * sine + localY * cosine
        };
    }

    private static void ApplyAnchorPosition(
        IWMControl control,
        WMDesignBounds bounds,
        MacCanvasInteraction interaction,
        double requestedWidth,
        double requestedHeight)
    {
        var centerDeltaX = double.IsFinite(interaction.CenterDeltaX) ? interaction.CenterDeltaX : 0;
        var centerDeltaY = double.IsFinite(interaction.CenterDeltaY) ? interaction.CenterDeltaY : 0;
        var x = bounds.X + centerDeltaX - (requestedWidth - bounds.Width) / 2d;
        var y = bounds.Y + centerDeltaY - (requestedHeight - bounds.Height) / 2d;
        control.Style.Left = WMStyleLength.Percent(x / bounds.ParentWidth * 100d);
        control.Style.Top = WMStyleLength.Percent(y / bounds.ParentHeight * 100d);
        control.Style.Right = null;
        control.Style.Bottom = null;
    }

    private static void SetWidth(IWMControl control, double pixels, double parentWidth) =>
        control.Style.Width = WMStyleLength.Percent(
            Math.Max(MinimumLayoutPixels, pixels) / parentWidth * 100d);

    private static void SetHeight(IWMControl control, double pixels, double parentHeight) =>
        control.Style.Height = WMStyleLength.Percent(
            Math.Max(MinimumLayoutPixels, pixels) / parentHeight * 100d);

    private static double ResolvePixels(WMStyleLength length, double parentSize, double fallback) =>
        length.IsAuto ? fallback : parentSize * length.Value / 100d;
}
