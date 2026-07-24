#nullable enable

using Newtonsoft.Json;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public static class WMControlTree
{
    public const int MaxContainerDepth = 2;

    public static IWMControl? Find(WMCanvas canvas, string id) =>
        Flatten(canvas).FirstOrDefault(control => control.ID == id);

    public static WMContainer? FindParent(WMCanvas canvas, string id)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var visited = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        foreach (var root in canvas.Children ?? [])
        {
            if (root is not WMContainer rootContainer) continue;
            var parent = FindParent(rootContainer, id, visited);
            if (parent != null) return parent;
        }

        return null;
    }

    public static IReadOnlyList<IWMControl> Flatten(WMCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var controls = new List<IWMControl>();
        var visited = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        foreach (var root in canvas.Children ?? [])
            Flatten(root, controls, visited);
        return controls;
    }

    public static bool CanMove(WMCanvas canvas, string controlId, string? parentId, out string error)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var control = Find(canvas, controlId);
        if (control == null)
        {
            error = "找不到要移动的控件。";
            return false;
        }

        WMContainer? parent = null;
        if (parentId != null)
        {
            parent = Find(canvas, parentId) as WMContainer;
            if (parent == null)
            {
                error = "目标容器不存在。";
                return false;
            }

            if (ReferenceEquals(control, parent) || ContainsReference(control, parent))
            {
                error = "不能移动到自身或其子级。";
                return false;
            }
        }
        if (control is WMContainer container)
        {
            var targetDepth = 0;
            if (parent != null && !TryGetContainerDepth(canvas, parent, out targetDepth))
            {
                error = "控件层级无效。";
                return false;
            }

            if (targetDepth + ContainerDepth(container) > MaxContainerDepth)
            {
                error = "容器只能保留根级和一层嵌套，共两级。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static bool Move(WMCanvas canvas, string controlId, string? parentId, int index)
    {
        if (!CanMove(canvas, controlId, parentId, out _)) return false;

        var control = Find(canvas, controlId)!;
        var target = parentId == null ? null : (WMContainer)Find(canvas, parentId)!;
        var sourceParent = FindParentByReference(canvas, control);
        if (!IsValidTargetIndex(canvas, control, target, index)) return false;
        if (!RemoveByReference(canvas, control)) return false;

        if (target == null)
        {
            canvas.Children.Insert(index, control);
            if (canvas.LayoutSchemaVersion >= WMLayoutMigration.CurrentSchemaVersion)
                control.Style.Position = WMPosition.Absolute;
        }
        else
        {
            target.Controls.Insert(index, control);
        }

        if (!ReferenceEquals(sourceParent, target))
        {
            var transform = control.EnsureTransform();
            transform.OffsetXPercent = 0;
            transform.OffsetYPercent = 0;
        }
        SynchronizeParentMetadata(canvas);

        return true;
    }

    public static bool MovePreservingVisualBounds(
        WMCanvas canvas,
        string controlId,
        string? parentId,
        int index,
        IReadOnlyList<WMDesignBounds> bounds,
        double canvasWidth,
        double canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(bounds);
        var control = Find(canvas, controlId);
        if (control is null) return false;
        var sourceParent = FindParent(canvas, controlId);
        var targetParent = parentId is null ? null : Find(canvas, parentId) as WMContainer;
        if (ReferenceEquals(sourceParent, targetParent))
            return Move(canvas, controlId, parentId, index);

        var byId = bounds.ToDictionary(item => item.ControlId, StringComparer.Ordinal);
        if (!byId.TryGetValue(controlId, out var sourceBounds))
            return Move(canvas, controlId, parentId, index);
        var sourceCenter = CanvasVisualCenter(sourceBounds, byId);
        var targetCenter = targetParent is not null
            && byId.TryGetValue(targetParent.ID, out var targetBounds)
                ? CanvasPointToLocal(sourceCenter, targetBounds, byId)
                : sourceCenter;
        var canvasUnit = Math.Min(canvasWidth, canvasHeight) / 100d;
        var padding = targetParent?.Style.Padding ?? new WMThickness(0);
        var targetWidth = targetParent is null
            ? canvasWidth
            : Math.Max(
                0,
                (byId.GetValueOrDefault(targetParent.ID)?.Width ?? 0)
                - (padding.Left + padding.Right) * canvasUnit);
        var targetHeight = targetParent is null
            ? canvasHeight
            : Math.Max(
                0,
                (byId.GetValueOrDefault(targetParent.ID)?.Height ?? 0)
                - (padding.Top + padding.Bottom) * canvasUnit);
        var mustBecomeAbsolute = targetParent is null || control.Style.Position == WMPosition.Absolute;

        if (!Move(canvas, controlId, parentId, index)) return false;
        if (!mustBecomeAbsolute || targetWidth <= 0 || targetHeight <= 0)
            return true;

        control.Style.Position = WMPosition.Absolute;
        control.Style.Left = WMStyleLength.Percent(
            (targetCenter.X - padding.Left * canvasUnit - sourceBounds.Width / 2d)
            / targetWidth * 100d);
        control.Style.Top = WMStyleLength.Percent(
            (targetCenter.Y - padding.Top * canvasUnit - sourceBounds.Height / 2d)
            / targetHeight * 100d);
        control.Style.Right = null;
        control.Style.Bottom = null;
        control.Style.Transform.OffsetXPercent = 0;
        control.Style.Transform.OffsetYPercent = 0;
        SynchronizeParentMetadata(canvas);
        return true;
    }

    private static (double X, double Y) CanvasVisualCenter(
        WMDesignBounds bounds,
        IReadOnlyDictionary<string, WMDesignBounds> byId)
    {
        var transform = bounds.Transform ?? new WMTransform();
        var point = (
            X: bounds.X + bounds.Width / 2d + bounds.ParentWidth * transform.OffsetXPercent / 100d,
            Y: bounds.Y + bounds.Height / 2d + bounds.ParentHeight * transform.OffsetYPercent / 100d);
        var parentId = bounds.ParentId;
        var visited = new HashSet<string>(StringComparer.Ordinal) { bounds.ControlId };
        while (parentId is not null
               && visited.Add(parentId)
               && byId.TryGetValue(parentId, out var parent))
        {
            point = LocalPointToParent(point, parent);
            parentId = parent.ParentId;
        }
        return point;
    }

    private static (double X, double Y) CanvasPointToLocal(
        (double X, double Y) canvasPoint,
        WMDesignBounds target,
        IReadOnlyDictionary<string, WMDesignBounds> byId)
    {
        var chain = new List<WMDesignBounds> { target };
        var parentId = target.ParentId;
        var visited = new HashSet<string>(StringComparer.Ordinal) { target.ControlId };
        while (parentId is not null
               && visited.Add(parentId)
               && byId.TryGetValue(parentId, out var parent))
        {
            chain.Add(parent);
            parentId = parent.ParentId;
        }

        var point = canvasPoint;
        for (var index = chain.Count - 1; index >= 0; index--)
            point = ParentPointToLocal(point, chain[index]);
        return point;
    }

    private static (double X, double Y) LocalPointToParent(
        (double X, double Y) point,
        WMDesignBounds node)
    {
        var transform = node.Transform ?? new WMTransform();
        var radians = transform.Rotation * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var scaledX = (point.X - node.Width / 2d) * transform.ScaleX;
        var scaledY = (point.Y - node.Height / 2d) * transform.ScaleY;
        return (
            node.X + node.Width / 2d
                + node.ParentWidth * transform.OffsetXPercent / 100d
                + scaledX * cosine - scaledY * sine,
            node.Y + node.Height / 2d
                + node.ParentHeight * transform.OffsetYPercent / 100d
                + scaledX * sine + scaledY * cosine);
    }

    private static (double X, double Y) ParentPointToLocal(
        (double X, double Y) point,
        WMDesignBounds node)
    {
        var transform = node.Transform ?? new WMTransform();
        var radians = -transform.Rotation * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var translatedX = point.X
            - node.X
            - node.Width / 2d
            - node.ParentWidth * transform.OffsetXPercent / 100d;
        var translatedY = point.Y
            - node.Y
            - node.Height / 2d
            - node.ParentHeight * transform.OffsetYPercent / 100d;
        var scaleX = Math.Abs(transform.ScaleX) < 0.000001 ? 1d : transform.ScaleX;
        var scaleY = Math.Abs(transform.ScaleY) < 0.000001 ? 1d : transform.ScaleY;
        return (
            node.Width / 2d + (translatedX * cosine - translatedY * sine) / scaleX,
            node.Height / 2d + (translatedX * sine + translatedY * cosine) / scaleY);
    }

    private static void SynchronizeParentMetadata(WMCanvas canvas)
    {
        for (var rootIndex = 0; rootIndex < canvas.Children.Count; rootIndex++)
        {
            var root = canvas.Children[rootIndex];
            root.PNode = new WMPNode(rootIndex, "0");
            if (root is WMContainer container)
                SynchronizeParentMetadata(container);
        }
    }

    private static void SynchronizeParentMetadata(WMContainer parent)
    {
        for (var index = 0; index < parent.Controls.Count; index++)
        {
            var child = parent.Controls[index];
            child.PNode = new WMPNode(index, parent.ID);
            if (child is WMContainer nested)
                SynchronizeParentMetadata(nested);
        }
    }

    public static IWMControl Duplicate(WMCanvas canvas, string controlId)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var source = Find(canvas, controlId)
            ?? throw new KeyNotFoundException("找不到要复制的控件。");
        var copy = CloneControl(source);
        var ids = Flatten(canvas).Select(control => control.ID).ToHashSet(StringComparer.Ordinal);
        AssignIds(copy, ids, new HashSet<IWMControl>(ReferenceEqualityComparer.Instance));

        var parent = FindParent(canvas, source.ID);
        if (parent == null)
        {
            canvas.Children.Insert(canvas.Children.IndexOf(source) + 1, copy);
        }
        else
        {
            parent.Controls.Insert(parent.Controls.IndexOf(source) + 1, copy);
        }

        SynchronizeParentMetadata(canvas);
        return copy;
    }

    public static bool Remove(WMCanvas canvas, string controlId)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var control = Find(canvas, controlId);
        if (control == null || !RemoveByReference(canvas, control)) return false;
        SynchronizeParentMetadata(canvas);
        return true;
    }

    public static IWMControl Add(WMCanvas canvas, Type controlType, string? preferredParentId)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(controlType);
        if (controlType != typeof(WMContainer)
            && controlType != typeof(WMText)
            && controlType != typeof(WMLogo)
            && controlType != typeof(WMLine))
            throw new ArgumentException("控件类型无效。", nameof(controlType));

        var control = (IWMControl?)Activator.CreateInstance(controlType)
            ?? throw new ArgumentException("无法创建控件。", nameof(controlType));
        if (control is WMText text && text.Exifs.Count == 0)
        {
            // A newly inserted text layer must have measurable content so it can
            // be selected and dragged immediately. Empty-key entries are the V2
            // representation for intentional decorative copy.
            text.Exifs.Add(new WMExifConfigInfo { Prefix = "文字" });
        }
        return Add(canvas, control, preferredParentId);
    }

    public static IWMControl Add(WMCanvas canvas, IWMControl control, string? preferredParentId)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(control);
        if (control is not WMContainer
            && control is not WMText
            && control is not WMLogo
            && control is not WMLine)
            throw new ArgumentException("控件类型无效。", nameof(control));

        var usedIds = Flatten(canvas).Select(existing => existing.ID).ToHashSet(StringComparer.Ordinal);
        AssignNewUniqueId(control, usedIds);
        var parent = preferredParentId == null ? null : Find(canvas, preferredParentId) as WMContainer;
        if (preferredParentId is not null && parent is null)
            throw new ArgumentException("目标容器不存在。", nameof(preferredParentId));

        if (canvas.LayoutSchemaVersion >= WMLayoutMigration.CurrentSchemaVersion)
            WMLayoutMigration.ApplyNewNodeDefaults(control, isRoot: parent is null);
        if (parent is null)
        {
            canvas.Children.Add(control);
        }
        else
        {
            if (control is WMContainer container
                && (!TryGetContainerDepth(canvas, parent, out var parentDepth)
                    || parentDepth + ContainerDepth(container) > MaxContainerDepth))
                throw new InvalidOperationException("容器只能保留根级和一层嵌套，共两级。");
            parent.Controls.Add(control);
        }
        SynchronizeParentMetadata(canvas);
        return control;
    }

    private static bool IsValidTargetIndex(WMCanvas canvas, IWMControl control, WMContainer? target, int index)
    {
        var targetCount = target?.Controls.Count ?? canvas.Children.Count;
        var alreadyInTarget = target == null
            ? canvas.Children.Contains(control)
            : target.Controls.Contains(control);
        var maximum = alreadyInTarget ? targetCount - 1 : targetCount;
        return index >= 0 && index <= maximum;
    }

    private static WMContainer? FindParent(WMContainer parent, string id, HashSet<IWMControl> visited)
    {
        if (!visited.Add(parent)) return null;
        foreach (var child in parent.Controls ?? [])
        {
            if (child.ID == id) return parent;
            if (child is WMContainer container)
            {
                var nestedParent = FindParent(container, id, visited);
                if (nestedParent != null) return nestedParent;
            }
        }

        return null;
    }

    private static void Flatten(IWMControl control, List<IWMControl> controls, HashSet<IWMControl> visited)
    {
        if (!visited.Add(control)) return;
        controls.Add(control);
        if (control is not WMContainer container) return;
        foreach (var child in container.Controls ?? [])
            Flatten(child, controls, visited);
    }

    private static bool ContainsReference(IWMControl root, IWMControl candidate)
    {
        var visited = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        return ContainsReference(root, candidate, visited);
    }

    private static bool ContainsReference(IWMControl root, IWMControl candidate, HashSet<IWMControl> visited)
    {
        if (!visited.Add(root)) return false;
        if (ReferenceEquals(root, candidate)) return true;
        return root is WMContainer container && (container.Controls ?? []).Any(child => ContainsReference(child, candidate, visited));
    }

    private static bool TryGetContainerDepth(WMCanvas canvas, WMContainer target, out int depth)
    {
        foreach (var root in canvas.Children ?? [])
        {
            if (root is WMContainer rootContainer
                && TryGetContainerDepth(rootContainer, target, 1, new HashSet<IWMControl>(ReferenceEqualityComparer.Instance), out depth))
                return true;
        }

        depth = 0;
        return false;
    }

    private static bool TryGetContainerDepth(WMContainer current, WMContainer target, int currentDepth, HashSet<IWMControl> active, out int depth)
    {
        if (!active.Add(current))
        {
            depth = 0;
            return false;
        }

        if (ReferenceEquals(current, target))
        {
            depth = currentDepth;
            return true;
        }

        foreach (var child in current.Controls?.OfType<WMContainer>() ?? [])
        {
            if (TryGetContainerDepth(child, target, currentDepth + 1, active, out depth))
                return true;
        }

        active.Remove(current);
        depth = 0;
        return false;
    }

    private static int ContainerDepth(WMContainer control)
    {
        var visited = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        return ContainerDepth(control, visited);
    }

    private static int ContainerDepth(WMContainer control, HashSet<IWMControl> visited)
    {
        if (!visited.Add(control)) return MaxContainerDepth + 1;
        var nested = (control.Controls ?? [])
            .OfType<WMContainer>()
            .Select(child => ContainerDepth(child, visited))
            .DefaultIfEmpty(0)
            .Max();
        return 1 + nested;
    }

    private static bool RemoveByReference(WMCanvas canvas, IWMControl control)
    {
        if (canvas.Children.Remove(control)) return true;
        var parent = FindParentByReference(canvas, control);
        return parent != null && parent.Controls.Remove(control);
    }

    private static WMContainer? FindParentByReference(WMCanvas canvas, IWMControl control)
    {
        var visited = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        foreach (var root in canvas.Children ?? [])
        {
            if (root is not WMContainer rootContainer) continue;
            var parent = FindParentByReference(rootContainer, control, visited);
            if (parent != null) return parent;
        }

        return null;
    }

    private static WMContainer? FindParentByReference(WMContainer parent, IWMControl control, HashSet<IWMControl> visited)
    {
        if (!visited.Add(parent)) return null;
        foreach (var child in parent.Controls ?? [])
        {
            if (ReferenceEquals(child, control)) return parent;
            if (child is WMContainer nested)
            {
                var nestedParent = FindParentByReference(nested, control, visited);
                if (nestedParent != null) return nestedParent;
            }
        }

        return null;
    }

    private static IWMControl CloneControl(IWMControl source)
    {
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        var json = JsonConvert.SerializeObject(source, settings);
        return (IWMControl?)JsonConvert.DeserializeObject(json, source.GetType(), settings)
            ?? throw new InvalidOperationException("无法复制控件。");
    }

    private static void AssignIds(IWMControl control, HashSet<string> usedIds, HashSet<IWMControl> visited)
    {
        if (!visited.Add(control)) return;
        string id;
        do id = Guid.NewGuid().ToString("N").ToUpperInvariant();
        while (!usedIds.Add(id));
        control.ID = id;

        if (control is WMContainer container)
            foreach (var child in container.Controls ?? [])
                AssignIds(child, usedIds, visited);
    }

    private static void AssignNewUniqueId(IWMControl control, HashSet<string> usedIds)
    {
        string id;
        do id = Guid.NewGuid().ToString("N").ToUpperInvariant();
        while (!usedIds.Add(id));
        control.ID = id;
    }
}
