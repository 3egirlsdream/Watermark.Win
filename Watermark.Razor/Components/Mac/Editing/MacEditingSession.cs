#nullable enable

using System.Security.Cryptography;
using SkiaSharp;
using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac.Editing;

public sealed record MacSessionHistoryEntry(
    WMImageOperation Operation,
    IReadOnlyDictionary<string, WMImageArtifact?> Before,
    IReadOnlyDictionary<string, WMImageArtifact> After,
    bool IsApplied);

public sealed record MacSessionChange(
    WMImageOperation? Operation,
    IReadOnlyDictionary<string, WMImageArtifact> CurrentArtifacts,
    IReadOnlyList<string> AddedMediaIds,
    IReadOnlyList<string> RemovedMediaIds)
{
    public static MacSessionChange Empty { get; } = new(null, new Dictionary<string, WMImageArtifact>(), [], []);
}

public sealed class MacEditingSession : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, WMImageArtifact> current = new(StringComparer.Ordinal);
    private readonly List<MacSessionHistoryEntry> undo = [];
    private readonly List<MacSessionHistoryEntry> redo = [];
    private bool disposed;

    public MacEditingSession() : this(Path.Combine(Global.AppPath.BasePath, "Cache", "editing-sessions"))
    {
    }

    public MacEditingSession(string sessionsRoot)
    {
        SessionsRoot = sessionsRoot;
        Directory.CreateDirectory(SessionsRoot);
        CleanupStaleSessions(SessionsRoot);
        SessionDirectory = Path.Combine(SessionsRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(SessionDirectory);
    }

    public string SessionsRoot { get; }
    public string SessionDirectory { get; }
    public bool CanUndo { get { lock (gate) return undo.Count > 0; } }
    public bool CanRedo { get { lock (gate) return redo.Count > 0; } }

    public IReadOnlyList<MacSessionHistoryEntry> History
    {
        get
        {
            lock (gate)
            {
                return undo.Concat(redo.AsEnumerable().Reverse()).ToArray();
            }
        }
    }

    public string GetWorkingDirectory(WMImageOperationKind kind, bool preview = false)
    {
        ThrowIfDisposed();
        var folder = Path.Combine(SessionDirectory, preview ? "preview" : "artifacts", kind.ToString().ToLowerInvariant());
        Directory.CreateDirectory(folder);
        return folder;
    }

    public WMImageArtifact RegisterSource(
        string mediaId,
        string path,
        IReadOnlyDictionary<string, string>? exif = null,
        string? previewPath = null)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(mediaId)) throw new ArgumentException("媒体 ID 不能为空。", nameof(mediaId));
        if (!File.Exists(path)) throw new FileNotFoundException("源图片不存在。", path);
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidOperationException($"无法读取图片：{Path.GetFileName(path)}");
        using var stream = File.OpenRead(path);
        var contentHash = Convert.ToHexString(SHA256.HashData(stream));
        var info = new FileInfo(path);
        var fingerprint = new WMSourceFingerprint
        {
            StableId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                $"{contentHash}|1|sRGB|{WMColorPipelineVersion.Current}"))),
            FileIdentity = Path.GetFullPath(path),
            ContentHash = contentHash,
            Length = info.Length,
            LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks,
            Orientation = 1,
            PipelineVersion = WMColorPipelineVersion.Current
        };
        var artifact = new WMImageArtifact
        {
            Id = Guid.NewGuid().ToString("N"),
            FilePath = path,
            PreviewPath = !string.IsNullOrWhiteSpace(previewPath) && File.Exists(previewPath) ? previewPath : null,
            ParentArtifactIds = [],
            SourceOperation = WMImageOperationKind.Source,
            Width = bitmap.Width,
            Height = bitmap.Height,
            Exif = exif ?? new Dictionary<string, string>(),
            ContentHash = contentHash,
            SourceFingerprint = fingerprint
        };
        lock (gate) current[mediaId] = artifact;
        return artifact;
    }

    public void Reset()
    {
        ThrowIfDisposed();
        lock (gate)
        {
            current.Clear();
            undo.Clear();
            redo.Clear();
            foreach (var directory in Directory.EnumerateDirectories(SessionDirectory)) TryDeleteDirectory(directory);
        }
    }

    public bool TryGetCurrent(string mediaId, out WMImageArtifact artifact)
    {
        lock (gate) return current.TryGetValue(mediaId, out artifact!);
    }

    public WMImageArtifact GetCurrent(string mediaId)
    {
        lock (gate)
        {
            return current.TryGetValue(mediaId, out var artifact)
                ? artifact
                : throw new KeyNotFoundException($"媒体 {mediaId} 尚未注册到编辑会话。");
        }
    }

    public MacRenderPlan BuildRenderPlan(string mediaId)
    {
        ThrowIfDisposed();
        lock (gate)
        {
            if (!current.TryGetValue(mediaId, out var artifact))
                throw new KeyNotFoundException($"媒体 {mediaId} 尚未注册到编辑会话。");

            var history = undo.Concat(redo).ToArray();
            var operations = history
                .GroupBy(entry => entry.Operation.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Operation, StringComparer.Ordinal);
            var artifacts = new Dictionary<string, WMImageArtifact>(StringComparer.Ordinal);
            foreach (var value in current.Values) artifacts[value.Id] = value;
            foreach (var entry in history)
            {
                foreach (var value in entry.Before.Values)
                    if (value is not null) artifacts[value.Id] = value;
                foreach (var value in entry.After.Values) artifacts[value.Id] = value;
            }

            var steps = new List<MacRenderPlanStep>();
            var cursor = artifact;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (cursor.SourceOperation is WMImageOperationKind.Template or WMImageOperationKind.ColorGrade)
            {
                if (!visited.Add(cursor.Id))
                    throw new InvalidOperationException("检测到循环的图片操作历史，无法导出。");
                if (string.IsNullOrWhiteSpace(cursor.OperationId)
                    || !operations.TryGetValue(cursor.OperationId, out var operation))
                    throw new InvalidOperationException("图片操作历史缺少对应的操作参数，无法导出。");
                if (cursor.ParentArtifactIds.Count != 1
                    || !artifacts.TryGetValue(cursor.ParentArtifactIds[0], out var parent))
                    throw new InvalidOperationException("图片操作历史缺少父产物，无法导出。");

                steps.Add(new MacRenderPlanStep(operation));
                cursor = parent;
            }

            steps.Reverse();
            return new MacRenderPlan(cursor, steps, artifact);
        }
    }

    public MacSessionChange Commit(
        WMImageOperation operation,
        IReadOnlyDictionary<string, WMImageArtifact> outputs,
        IReadOnlyCollection<string>? createdMediaIds = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(operation);
        if (outputs.Count == 0) throw new ArgumentException("操作没有输出产物。", nameof(outputs));
        foreach (var output in outputs.Values)
        {
            if (!File.Exists(output.FilePath)) throw new FileNotFoundException("操作输出不存在，批次未提交。", output.FilePath);
        }

        var created = createdMediaIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        lock (gate)
        {
            var before = new Dictionary<string, WMImageArtifact?>(StringComparer.Ordinal);
            foreach (var pair in outputs)
            {
                if (!created.Contains(pair.Key) && !current.ContainsKey(pair.Key))
                    throw new InvalidOperationException($"媒体 {pair.Key} 尚未注册，无法提交原子操作。");
                before[pair.Key] = current.GetValueOrDefault(pair.Key);
            }

            DeleteAbandonedRedoArtifacts();
            redo.Clear();
            foreach (var pair in outputs) current[pair.Key] = pair.Value;
            var entry = new MacSessionHistoryEntry(operation, before, new Dictionary<string, WMImageArtifact>(outputs), true);
            undo.Add(entry);
            return CreateChange(entry, added: created.ToArray(), removed: []);
        }
    }

    public MacSessionChange ReplaceLast(
        string expectedOperationId,
        WMImageOperation replacementOperation,
        IReadOnlyDictionary<string, WMImageArtifact> outputs)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(replacementOperation);
        foreach (var output in outputs.Values)
            if (!File.Exists(output.FilePath)) throw new FileNotFoundException("替换操作输出不存在。", output.FilePath);

        lock (gate)
        {
            if (undo.Count == 0 || undo[^1].Operation.Id != expectedOperationId)
                throw new InvalidOperationException("当前调色操作已被冻结，不能再替换历史节点。");
            var previous = undo[^1];
            if (previous.Operation.Kind != WMImageOperationKind.ColorGrade)
                throw new InvalidOperationException("只能替换当前调色历史节点。");
            if (!previous.After.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(outputs.Keys))
                throw new InvalidOperationException("替换调色操作的素材范围必须保持不变。");

            DeleteAbandonedRedoArtifacts();
            redo.Clear();
            foreach (var artifact in previous.After.Values)
            {
                if (IsSessionFile(artifact.FilePath)) TryDelete(artifact.FilePath);
                if (!string.IsNullOrWhiteSpace(artifact.PreviewPath) && IsSessionFile(artifact.PreviewPath))
                    TryDelete(artifact.PreviewPath);
            }
            foreach (var pair in outputs) current[pair.Key] = pair.Value;
            var entry = new MacSessionHistoryEntry(
                replacementOperation,
                previous.Before,
                new Dictionary<string, WMImageArtifact>(outputs),
                true);
            undo[^1] = entry;
            return CreateChange(entry, [], []);
        }
    }

    public MacSessionChange Undo()
    {
        ThrowIfDisposed();
        lock (gate)
        {
            if (undo.Count == 0) return MacSessionChange.Empty;
            var entry = undo[^1];
            undo.RemoveAt(undo.Count - 1);
            var removed = new List<string>();
            foreach (var pair in entry.Before)
            {
                if (pair.Value == null)
                {
                    current.Remove(pair.Key);
                    removed.Add(pair.Key);
                }
                else
                {
                    current[pair.Key] = pair.Value;
                }
            }
            redo.Add(entry with { IsApplied = false });
            return CreateChange(entry, added: [], removed);
        }
    }

    public MacSessionChange Redo()
    {
        ThrowIfDisposed();
        lock (gate)
        {
            if (redo.Count == 0) return MacSessionChange.Empty;
            var entry = redo[^1];
            redo.RemoveAt(redo.Count - 1);
            var added = entry.Before.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray();
            foreach (var pair in entry.After) current[pair.Key] = pair.Value;
            undo.Add(entry with { IsApplied = true });
            return CreateChange(entry, added, removed: []);
        }
    }

    public void DeletePreviewFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths) TryDelete(path);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        TryDeleteDirectory(SessionDirectory);
    }

    private MacSessionChange CreateChange(MacSessionHistoryEntry entry, IReadOnlyList<string> added, IReadOnlyList<string> removed) =>
        new(
            entry.Operation,
            entry.After.Keys
                .Where(current.ContainsKey)
                .ToDictionary(mediaId => mediaId, mediaId => current[mediaId], StringComparer.Ordinal),
            added,
            removed);

    private void DeleteAbandonedRedoArtifacts()
    {
        foreach (var artifact in redo.SelectMany(entry => entry.After.Values))
        {
            if (IsSessionFile(artifact.FilePath)) TryDelete(artifact.FilePath);
            if (!string.IsNullOrWhiteSpace(artifact.PreviewPath) && IsSessionFile(artifact.PreviewPath)) TryDelete(artifact.PreviewPath);
            if (artifact.HighPrecision is { FilePath: var highPrecisionPath } && IsSessionFile(highPrecisionPath))
                TryDelete(highPrecisionPath);
        }
    }

    private bool IsSessionFile(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(SessionDirectory), StringComparison.Ordinal);

    private static void CleanupStaleSessions(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-1))
                    Directory.Delete(directory, true);
            }
            catch { }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
