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
            var parent = FindParent(root, id, visited);
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
        else if (control is not WMContainer)
        {
            error = "只有容器可以移动到根级。";
            return false;
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
            var root = (WMContainer)control;
            canvas.Children.Insert(index, root);
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

    private static void SynchronizeParentMetadata(WMCanvas canvas)
    {
        for (var rootIndex = 0; rootIndex < canvas.Children.Count; rootIndex++)
        {
            var root = canvas.Children[rootIndex];
            root.PNode = new WMPNode(rootIndex, "0");
            SynchronizeParentMetadata(root);
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
            var root = source as WMContainer
                ?? throw new InvalidOperationException("非容器控件不能位于根级。");
            canvas.Children.Insert(canvas.Children.IndexOf(root) + 1, (WMContainer)copy);
        }
        else
        {
            parent.Controls.Insert(parent.Controls.IndexOf(source) + 1, copy);
        }

        return copy;
    }

    public static bool Remove(WMCanvas canvas, string controlId)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var control = Find(canvas, controlId);
        return control != null && RemoveByReference(canvas, control);
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
        var usedIds = Flatten(canvas).Select(existing => existing.ID).ToHashSet(StringComparer.Ordinal);
        AssignNewUniqueId(control, usedIds);
        if (control is WMContainer container)
        {
            if (canvas.LayoutSchemaVersion >= WMLayoutMigration.CurrentSchemaVersion)
                WMLayoutMigration.ApplyNewNodeDefaults(container, isRoot: true);
            canvas.Children.Add(container);
            return container;
        }

        var parent = preferredParentId == null ? null : Find(canvas, preferredParentId) as WMContainer;
        if (parent == null)
        {
            parent = new WMContainer { Name = "新容器" };
            AssignNewUniqueId(parent, usedIds);
            if (canvas.LayoutSchemaVersion >= WMLayoutMigration.CurrentSchemaVersion)
                WMLayoutMigration.ApplyNewNodeDefaults(parent, isRoot: true);
            canvas.Children.Add(parent);
        }

        if (canvas.LayoutSchemaVersion >= WMLayoutMigration.CurrentSchemaVersion)
            WMLayoutMigration.ApplyNewNodeDefaults(control);
        parent.Controls.Add(control);
        return control;
    }

    private static bool IsValidTargetIndex(WMCanvas canvas, IWMControl control, WMContainer? target, int index)
    {
        var targetCount = target?.Controls.Count ?? canvas.Children.Count;
        var alreadyInTarget = target == null
            ? control is WMContainer root && canvas.Children.Contains(root)
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
            if (TryGetContainerDepth(root, target, 1, new HashSet<IWMControl>(ReferenceEqualityComparer.Instance), out depth))
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
        if (control is WMContainer root && canvas.Children.Remove(root)) return true;
        var parent = FindParentByReference(canvas, control);
        return parent != null && parent.Controls.Remove(control);
    }

    private static WMContainer? FindParentByReference(WMCanvas canvas, IWMControl control)
    {
        var visited = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        foreach (var root in canvas.Children ?? [])
        {
            var parent = FindParentByReference(root, control, visited);
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
