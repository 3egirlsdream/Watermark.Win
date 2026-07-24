#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed record WMTemplateControlGeometry(
    string Id,
    double Width,
    double Height,
    double DesignX,
    double DesignY);

public sealed record WMTemplateEditorSnapshotState(
    string Json,
    string Path,
    IReadOnlyDictionary<string, Dictionary<string, string>> Exif,
    IReadOnlyList<WMTemplateControlGeometry> Geometry);

public sealed record WMTemplateEditorDraftState(
    IReadOnlyList<WMTemplateEditorSnapshotState> History,
    int HistoryCursor,
    string SavedSnapshot,
    string? SelectedControlId);

public sealed class WMTemplateEditorState
{
    private const int HistoryLimit = 50;
    private readonly List<Snapshot> history;
    private int historyIndex;
    private string savedSnapshot;
    private Snapshot? transactionStart;
    private string transactionLabel = string.Empty;
    private WMTemplateChangeKind transactionKind;
    private readonly HashSet<string> transactionNodeIds = new(StringComparer.Ordinal);
    private long revision;

    private WMTemplateEditorState(WMCanvas draft)
    {
        Draft = draft;
        var initial = Snapshot.From(draft);
        history = [initial];
        savedSnapshot = initial.Json;
    }

    public WMCanvas Draft { get; private set; }
    public string? SelectedControlId { get; private set; }
    public bool IsDirty => Serialize(Draft) != savedSnapshot;
    public bool CanUndo => historyIndex > 0;
    public bool CanRedo => historyIndex < history.Count - 1;
    public int HistoryCount => history.Count;
    public int HistoryCursor => historyIndex;
    public bool IsTransactionActive => transactionStart is not null;
    public long CurrentRevision => revision;
    public event Action? Changed;
    public event Action<WMTemplateChangeSet>? DetailedChanged;

    public static WMTemplateEditorState Create(WMCanvas original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var draft = Clone(original);
        // Migration is intentionally editor-local. Reading a legacy template for
        // export still uses its compatibility renderer, while opening it in the
        // editor produces a V2 draft without marking it dirty until the user
        // actually changes it or saves it.
        WMLayoutMigration.UpgradeInMemory(draft);
        return new WMTemplateEditorState(draft);
    }

    public static WMTemplateEditorState Restore(
        WMCanvas original,
        WMTemplateEditorDraftState persisted)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(persisted);
        if (persisted.History.Count == 0)
            return Create(original);

        var snapshots = persisted.History
            .Take(HistoryLimit)
            .Select(Snapshot.FromState)
            .ToList();
        var cursor = Math.Clamp(persisted.HistoryCursor, 0, snapshots.Count - 1);
        var restored = new WMTemplateEditorState(snapshots[cursor].Restore())
        {
            historyIndex = cursor,
            savedSnapshot = persisted.SavedSnapshot,
            SelectedControlId = persisted.SelectedControlId
        };
        restored.history.Clear();
        restored.history.AddRange(snapshots);
        if (!string.IsNullOrWhiteSpace(restored.SelectedControlId)
            && WMControlTree.Find(restored.Draft, restored.SelectedControlId) is null)
            restored.SelectedControlId = null;
        return restored;
    }

    public bool TryExportDraftState(out WMTemplateEditorDraftState? persisted)
    {
        if (transactionStart is not null)
        {
            persisted = null;
            return false;
        }

        persisted = new WMTemplateEditorDraftState(
            history.Select(snapshot => snapshot.ToState()).ToArray(),
            historyIndex,
            savedSnapshot,
            SelectedControlId);
        return true;
    }

    public void Select(string? controlId)
    {
        if (SelectedControlId == controlId) return;
        SelectedControlId = controlId;
        PublishChange(
            WMTemplateChangePhase.Commit,
            WMTemplateChangeKind.Selection,
            string.IsNullOrWhiteSpace(controlId) ? [] : [controlId],
            "选择图层");
    }

    public void BeginTransaction(
        string label,
        WMTemplateChangeKind kind = WMTemplateChangeKind.All,
        IEnumerable<string>? nodeIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (transactionStart != null)
            throw new InvalidOperationException("已有正在进行的编辑事务。");

        transactionStart = Snapshot.From(Draft);
        transactionLabel = label;
        transactionKind = kind;
        transactionNodeIds.Clear();
        AddNodeIds(transactionNodeIds, nodeIds);
        PublishChange(
            WMTemplateChangePhase.Begin,
            kind,
            transactionNodeIds,
            label,
            incrementRevision: false);
    }

    public bool CommitTransaction(
        WMTemplateChangeKind additionalKind = WMTemplateChangeKind.None)
    {
        if (transactionStart == null) return false;

        var start = transactionStart;
        var label = transactionLabel;
        var kind = transactionKind | additionalKind;
        var nodeIds = transactionNodeIds.ToArray();
        transactionStart = null;
        ResetTransactionDetails();
        if (start.Matches(Draft))
        {
            // BeginTransaction publishes the active transaction state to the
            // workspace toolbar. A no-op commit still has to publish that the
            // transaction ended, but must not schedule a scene update.
            Changed?.Invoke();
            return false;
        }

        var committed = CommitCurrentSnapshot();
        if (committed)
            PublishChange(WMTemplateChangePhase.Commit, kind, nodeIds, label);
        return committed;
    }

    public void CancelTransaction()
    {
        if (transactionStart == null) return;

        var start = transactionStart;
        var label = transactionLabel;
        var kind = transactionKind;
        var nodeIds = transactionNodeIds.ToArray();
        transactionStart = null;
        ResetTransactionDetails();
        if (start.Matches(Draft))
        {
            PublishChange(
                WMTemplateChangePhase.Cancel,
                kind,
                nodeIds,
                label,
                incrementRevision: false);
            return;
        }

        Draft = start.Restore();
        PublishChange(WMTemplateChangePhase.Cancel, kind, nodeIds, label);
    }

    public void Mutate(
        string label,
        Action mutation,
        WMTemplateChangeKind kind = WMTemplateChangeKind.All,
        IEnumerable<string>? nodeIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(mutation);

        var before = Snapshot.From(Draft);
        mutation();
        if (before.Matches(Draft)) return;

        if (transactionStart == null)
        {
            CommitCurrentSnapshot();
            PublishChange(
                WMTemplateChangePhase.Commit,
                kind,
                nodeIds?.ToArray() ?? [],
                label);
        }
        else
        {
            transactionKind |= kind;
            AddNodeIds(transactionNodeIds, nodeIds);
            PublishChange(
                WMTemplateChangePhase.Update,
                transactionKind,
                transactionNodeIds,
                transactionLabel,
                incrementRevision: true);
        }
    }

    public bool Undo()
    {
        EnsureNoActiveTransaction();
        if (!CanUndo) return false;

        historyIndex--;
        Draft = history[historyIndex].Restore();
        PublishChange(WMTemplateChangePhase.Commit, WMTemplateChangeKind.All, [], "撤销");
        return true;
    }

    public bool Redo()
    {
        EnsureNoActiveTransaction();
        if (!CanRedo) return false;

        historyIndex++;
        Draft = history[historyIndex].Restore();
        PublishChange(WMTemplateChangePhase.Commit, WMTemplateChangeKind.All, [], "重做");
        return true;
    }

    public void MarkSaved()
    {
        savedSnapshot = Serialize(Draft);
        Changed?.Invoke();
    }

    private void PublishChange(
        WMTemplateChangePhase phase,
        WMTemplateChangeKind kind,
        IEnumerable<string> nodeIds,
        string label,
        bool incrementRevision = true)
    {
        if (incrementRevision) revision++;
        Changed?.Invoke();
        DetailedChanged?.Invoke(new WMTemplateChangeSet(
            revision,
            phase,
            kind,
            nodeIds.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            label));
    }

    private static void AddNodeIds(
        ISet<string> destination,
        IEnumerable<string>? nodeIds)
    {
        if (nodeIds is null) return;
        foreach (var nodeId in nodeIds)
        {
            if (!string.IsNullOrWhiteSpace(nodeId))
                destination.Add(nodeId);
        }
    }

    private void ResetTransactionDetails()
    {
        transactionLabel = string.Empty;
        transactionKind = WMTemplateChangeKind.None;
        transactionNodeIds.Clear();
    }

    private bool CommitCurrentSnapshot()
    {
        var snapshot = Snapshot.From(Draft);
        if (snapshot.IsEquivalentTo(history[historyIndex])) return false;

        if (historyIndex < history.Count - 1)
            history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);

        history.Add(snapshot);
        historyIndex = history.Count - 1;
        if (history.Count > HistoryLimit)
        {
            history.RemoveAt(0);
            historyIndex--;
        }

        return true;
    }

    private void EnsureNoActiveTransaction()
    {
        if (transactionStart != null)
            throw new InvalidOperationException("请先提交或取消当前编辑事务。");
    }

    private static string Serialize(WMCanvas canvas) => Global.CanvasSerialize(canvas);

    private static WMCanvas Clone(WMCanvas source)
    {
        var copy = Global.ReadConfig(Global.CanvasSerialize(source));
        copy.Path = source.Path;
        copy.Exif = source.Exif.ToDictionary(
            pair => pair.Key,
            pair => new Dictionary<string, string>(pair.Value));
        CopyRuntimeGeometry(source, copy);
        return copy;
    }

    private static void CopyRuntimeGeometry(WMCanvas source, WMCanvas target)
    {
        var sourceControls = WMControlTree.Flatten(source);
        var targetControls = WMControlTree.Flatten(target);
        foreach (var pair in sourceControls.Zip(targetControls))
        {
            pair.Second.Width = pair.First.Width;
            pair.Second.Height = pair.First.Height;
            pair.Second.DesignX = pair.First.DesignX;
            pair.Second.DesignY = pair.First.DesignY;
        }
    }

    private sealed record Snapshot(
        string Json,
        string Path,
        Dictionary<string, Dictionary<string, string>> Exif,
        IReadOnlyList<ControlGeometry> Geometry)
    {
        public static Snapshot From(WMCanvas canvas) => new(
            Serialize(canvas),
            canvas.Path,
            canvas.Exif.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value)),
            WMControlTree.Flatten(canvas)
                .Select(control => new ControlGeometry(control.ID, control.Width, control.Height, control.DesignX, control.DesignY))
                .ToList());

        public bool Matches(WMCanvas canvas) => IsEquivalentTo(From(canvas));

        public bool IsEquivalentTo(Snapshot other) =>
            Json == other.Json
            && Path == other.Path
            && ExifEqual(Exif, other.Exif)
            && Geometry.SequenceEqual(other.Geometry);

        public WMCanvas Restore()
        {
            var canvas = Global.ReadConfig(Json);
            canvas.Path = Path;
            canvas.Exif = Exif.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value));
            var controls = WMControlTree.Flatten(canvas);
            foreach (var pair in controls.Zip(Geometry))
            {
                pair.First.Width = pair.Second.Width;
                pair.First.Height = pair.Second.Height;
                pair.First.DesignX = pair.Second.DesignX;
                pair.First.DesignY = pair.Second.DesignY;
            }
            return canvas;
        }

        public WMTemplateEditorSnapshotState ToState() =>
            new(
                Json,
                Path,
                Exif.ToDictionary(
                    pair => pair.Key,
                    pair => new Dictionary<string, string>(pair.Value)),
                Geometry.Select(item => new WMTemplateControlGeometry(
                    item.Id,
                    item.Width,
                    item.Height,
                    item.DesignX,
                    item.DesignY)).ToArray());

        public static Snapshot FromState(WMTemplateEditorSnapshotState state) =>
            new(
                state.Json,
                state.Path,
                state.Exif.ToDictionary(
                    pair => pair.Key,
                    pair => new Dictionary<string, string>(pair.Value)),
                state.Geometry.Select(item => new ControlGeometry(
                    item.Id,
                    item.Width,
                    item.Height,
                    item.DesignX,
                    item.DesignY)).ToArray());

        private static bool ExifEqual(
            IReadOnlyDictionary<string, Dictionary<string, string>> left,
            IReadOnlyDictionary<string, Dictionary<string, string>> right) =>
            left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out var value)
                && pair.Value.Count == value.Count
                && pair.Value.All(inner => value.TryGetValue(inner.Key, out var innerValue) && inner.Value == innerValue));
    }

    private sealed record ControlGeometry(string Id, double Width, double Height, double DesignX, double DesignY);
}
