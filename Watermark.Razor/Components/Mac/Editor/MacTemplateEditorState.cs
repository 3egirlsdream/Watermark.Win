#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac.Editor;

public sealed class MacTemplateEditorState
{
    private const int HistoryLimit = 50;
    private readonly List<Snapshot> history;
    private int historyIndex;
    private string savedSnapshot;
    private Snapshot? transactionStart;

    private MacTemplateEditorState(WMCanvas draft)
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
    public event Action? Changed;

    public static MacTemplateEditorState Create(WMCanvas original)
    {
        ArgumentNullException.ThrowIfNull(original);
        return new MacTemplateEditorState(Clone(original));
    }

    public void Select(string? controlId)
    {
        if (SelectedControlId == controlId) return;
        SelectedControlId = controlId;
        Changed?.Invoke();
    }

    public void BeginTransaction(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (transactionStart != null)
            throw new InvalidOperationException("已有正在进行的编辑事务。");

        transactionStart = Snapshot.From(Draft);
    }

    public bool CommitTransaction()
    {
        if (transactionStart == null) return false;

        transactionStart = null;
        var committed = CommitCurrentSnapshot();
        if (committed) Changed?.Invoke();
        return committed;
    }

    public void CancelTransaction()
    {
        if (transactionStart == null) return;

        var start = transactionStart;
        transactionStart = null;
        if (Serialize(Draft) == start.Json) return;

        Draft = start.Restore();
        Changed?.Invoke();
    }

    public void Mutate(string label, Action mutation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(mutation);

        var before = Serialize(Draft);
        mutation();
        if (Serialize(Draft) == before) return;

        if (transactionStart == null)
            CommitCurrentSnapshot();

        Changed?.Invoke();
    }

    public bool Undo()
    {
        EnsureNoActiveTransaction();
        if (!CanUndo) return false;

        historyIndex--;
        Draft = history[historyIndex].Restore();
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        EnsureNoActiveTransaction();
        if (!CanRedo) return false;

        historyIndex++;
        Draft = history[historyIndex].Restore();
        Changed?.Invoke();
        return true;
    }

    public void MarkSaved()
    {
        savedSnapshot = Serialize(Draft);
        Changed?.Invoke();
    }

    private bool CommitCurrentSnapshot()
    {
        var snapshot = Snapshot.From(Draft);
        if (snapshot.Json == history[historyIndex].Json) return false;

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
        return copy;
    }

    private sealed record Snapshot(string Json, string Path, Dictionary<string, Dictionary<string, string>> Exif)
    {
        public static Snapshot From(WMCanvas canvas) => new(
            Serialize(canvas),
            canvas.Path,
            canvas.Exif.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value)));

        public WMCanvas Restore()
        {
            var canvas = Global.ReadConfig(Json);
            canvas.Path = Path;
            canvas.Exif = Exif.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value));
            return canvas;
        }
    }
}
