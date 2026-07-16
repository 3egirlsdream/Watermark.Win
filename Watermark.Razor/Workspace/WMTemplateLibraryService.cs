#nullable enable

using System.Security.Cryptography;
using System.Text;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public enum WMTemplateLoadState
{
    PreviewPending,
    Ready,
    Error
}

public sealed class WMTemplateLibraryEntry
{
    public required string Key { get; init; }
    public required string TemplateId { get; init; }
    public required string FolderPath { get; init; }
    public required string ConfigPath { get; init; }
    public required string Fingerprint { get; init; }
    public required WMCanvas Canvas { get; init; }
    public string PreviewPath { get; set; } = string.Empty;
    public string PreviewSrc { get; set; } = string.Empty;
    public WMTemplateLoadState LoadState { get; set; } = WMTemplateLoadState.PreviewPending;
    public string ErrorMessage { get; set; } = string.Empty;
    internal long PreviewVersion { get; init; }
    internal WMObjectUrlLease? PreviewLease { get; set; }

    public WMTemplateList ToTemplateList() => new()
    {
        ID = TemplateId,
        Canvas = Canvas,
        Src = PreviewSrc
    };
}

/// <summary>
/// Incremental local template index. Configurations are published immediately;
/// one background worker fills fingerprinted disk previews and Blob leases.
/// </summary>
public class WMTemplateLibraryService : IAsyncDisposable
{
    private const int PreviewPipelineVersion = 2;
    private readonly IWMWatermarkHelper helper;
    private readonly IWMObjectUrlRegistry objectUrls;
    private readonly IWMWorkspacePerformanceCounters metrics;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly Dictionary<string, WMTemplateLibraryEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> invalidated = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> previewQueue = new();
    private readonly HashSet<string> queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly object queueGate = new();
    private readonly string previewRoot;
    private bool workerRunning;
    private CancellationTokenSource workerCancellation = new();
    private Task? workerTask;
    private long nextPreviewVersion;
    private bool disposed;

    public WMTemplateLibraryService(
        IWMWatermarkHelper helper,
        IWMObjectUrlRegistry objectUrls,
        IWMWorkspacePerformanceCounters metrics)
    {
        this.helper = helper;
        this.objectUrls = objectUrls;
        this.metrics = metrics;
        previewRoot = Path.Combine(Global.AppPath.BasePath, "Cache", "template-previews");
    }

    public event Action Changed = delegate { };

    public IReadOnlyList<WMTemplateList> Templates
    {
        get
        {
            lock (entries)
            {
                return entries.Values
                    .OrderBy(item => item.Canvas.CanvasType)
                    .ThenBy(item => item.Canvas.Name)
                    .Select(item => item.ToTemplateList())
                    .ToArray();
            }
        }
    }

    public async Task<IReadOnlyList<WMTemplateList>> GetOrRefreshAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (force) await ForceReloadAsync(cancellationToken).ConfigureAwait(false);
        else await RefreshChangedAsync(cancellationToken).ConfigureAwait(false);
        return Templates;
    }

    public async Task ForceReloadAsync(CancellationToken cancellationToken = default)
    {
        await StopWorkerAsync().ConfigureAwait(false);
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClearQueue();
            WMTemplateLibraryEntry[] previous;
            lock (entries)
            {
                previous = entries.Values.ToArray();
                entries.Clear();
            }
            foreach (var entry in previous)
                await ReleaseEntryLeaseAsync(entry).ConfigureAwait(false);
            invalidated.Clear();
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            refreshLock.Release();
        }
        StartWorker();
    }

    public async Task RefreshChangedAsync(CancellationToken cancellationToken = default)
    {
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            refreshLock.Release();
        }
        StartWorker();
    }

    public void Invalidate(string templateId)
    {
        if (!string.IsNullOrWhiteSpace(templateId)) invalidated.Add(templateId);
    }

    public Task EnsurePreviewAsync(string templateId)
    {
        string? key;
        lock (entries)
            key = entries.Values.FirstOrDefault(item => item.TemplateId == templateId)?.Key;
        if (key is not null)
        {
            QueuePreview(key);
            StartWorker();
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        ClearQueue();
        await StopWorkerAsync(recreate: false).ConfigureAwait(false);
        WMTemplateLibraryEntry[] snapshot;
        lock (entries) snapshot = entries.Values.ToArray();
        foreach (var entry in snapshot)
            await ReleaseEntryLeaseAsync(entry).ConfigureAwait(false);
        workerCancellation.Dispose();
        refreshLock.Dispose();
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Global.AppPath.TemplatesFolder);
        Directory.CreateDirectory(previewRoot);
        var folders = new DirectoryInfo(Global.AppPath.TemplatesFolder)
            .EnumerateDirectories()
            .Where(folder => File.Exists(Path.Combine(folder.FullName, "config.json")))
            .ToDictionary(folder => folder.Name, StringComparer.OrdinalIgnoreCase);

        string[] removed;
        lock (entries) removed = entries.Keys.Except(folders.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var key in removed)
        {
            WMTemplateLibraryEntry? entry;
            lock (entries)
            {
                entries.Remove(key, out entry);
            }
            if (entry is not null) await ReleaseEntryLeaseAsync(entry).ConfigureAwait(false);
        }

        foreach (var pair in folders.OrderBy(item => item.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configPath = Path.Combine(pair.Value.FullName, "config.json");
            var fingerprint = await CreateFingerprintAsync(pair.Value.FullName, cancellationToken).ConfigureAwait(false);
            WMTemplateLibraryEntry? existing;
            lock (entries) entries.TryGetValue(pair.Key, out existing);
            if (existing is not null
                && existing.Fingerprint == fingerprint
                && !invalidated.Contains(pair.Key)
                && !invalidated.Contains(existing.TemplateId))
                continue;

            if (existing is not null)
                await ReleaseEntryLeaseAsync(existing).ConfigureAwait(false);
            try
            {
                var canvas = await Task.Run(() => Global.ReadConfigFromPath(configPath), cancellationToken)
                    .ConfigureAwait(false);
                canvas.Exif[canvas.ID] = ExifHelper.DefaultMeta;
                await Global.InitFonts([canvas]).ConfigureAwait(false);
                var cachePath = Path.Combine(previewRoot, $"{fingerprint}.png");
                var entry = new WMTemplateLibraryEntry
                {
                    Key = pair.Key,
                    TemplateId = canvas.ID,
                    FolderPath = pair.Value.FullName,
                    ConfigPath = configPath,
                    Fingerprint = fingerprint,
                    Canvas = canvas,
                    PreviewPath = cachePath,
                    PreviewVersion = Interlocked.Increment(ref nextPreviewVersion)
                };
                lock (entries) entries[pair.Key] = entry;
                invalidated.Remove(pair.Key);
                invalidated.Remove(canvas.ID);
                // Mobile creates previews only when a card becomes visible. Desktop
                // keeps eager background generation for its persistent side panel.
                if (!Global.IsMobile) QueuePreview(pair.Key);
                Changed.Invoke();
            }
            catch (Exception ex)
            {
                if (existing is not null)
                {
                    existing.LoadState = WMTemplateLoadState.Error;
                    existing.ErrorMessage = ex.Message;
                }
            }
        }
        Changed.Invoke();
    }

    private void QueuePreview(string key)
    {
        lock (queueGate)
        {
            if (!queued.Add(key)) return;
            previewQueue.Enqueue(key);
        }
    }

    private void StartWorker()
    {
        lock (queueGate)
        {
            if (disposed || workerRunning || previewQueue.Count == 0) return;
            workerRunning = true;
        }
        workerTask = ProcessQueueAsync(workerCancellation.Token);
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!disposed && !cancellationToken.IsCancellationRequested)
            {
                string key;
                lock (queueGate)
                {
                    if (previewQueue.Count == 0) return;
                    key = previewQueue.Dequeue();
                    queued.Remove(key);
                }
                WMTemplateLibraryEntry? entry;
                lock (entries) entries.TryGetValue(key, out entry);
                if (entry is null || entry.LoadState == WMTemplateLoadState.Ready) continue;
                await GenerateOrLoadPreviewAsync(key, entry, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (queueGate) workerRunning = false;
        }
    }

    private async Task GenerateOrLoadPreviewAsync(
        string key,
        WMTemplateLibraryEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes;
            if (File.Exists(entry.PreviewPath))
            {
                bytes = await File.ReadAllBytesAsync(entry.PreviewPath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using (metrics.Measure(WMWorkspaceMetricStage.Replay))
                {
#pragma warning disable CS8625
                    bytes = await helper.GenerationAsync(entry.Canvas, null, true, designMode: true).WaitAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore CS8625
                }
                if (bytes.Length == 0) throw new InvalidOperationException("模板预览结果为空。");
                metrics.Increment(WMWorkspaceMetricStage.Encode);
                var temporary = entry.PreviewPath + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                    File.Move(temporary, entry.PreviewPath, true);
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
            await using var content = new MemoryStream(bytes, writable: false);
            var lease = await objectUrls.PublishAsync(
                Owner(entry.TemplateId),
                entry.PreviewVersion,
                content,
                "image/png",
                cancellationToken).ConfigureAwait(false);
            if (lease is null) return;
            var accepted = false;
            lock (entries)
            {
                if (entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                {
                    accepted = true;
                    entry.PreviewLease = lease;
                    entry.PreviewSrc = lease.Url;
                    entry.LoadState = WMTemplateLoadState.Ready;
                    entry.ErrorMessage = string.Empty;
                }
            }
            if (!accepted) await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            entry.LoadState = WMTemplateLoadState.Error;
            entry.ErrorMessage = ex.Message;
        }
        Changed.Invoke();
    }

    private void ClearQueue()
    {
        lock (queueGate)
        {
            previewQueue.Clear();
            queued.Clear();
        }
    }

    private async Task StopWorkerAsync(bool recreate = true)
    {
        Task? current;
        CancellationTokenSource cancellation;
        lock (queueGate)
        {
            cancellation = workerCancellation;
            cancellation.Cancel();
            current = workerTask;
        }
        if (current is not null)
        {
            try { await current.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        lock (queueGate)
        {
            if (ReferenceEquals(workerCancellation, cancellation) && recreate)
            {
                workerCancellation.Dispose();
                workerCancellation = new CancellationTokenSource();
                workerTask = null;
                workerRunning = false;
            }
        }
    }

    private async ValueTask ReleaseEntryLeaseAsync(WMTemplateLibraryEntry entry)
    {
        var lease = entry.PreviewLease;
        entry.PreviewLease = null;
        entry.PreviewSrc = string.Empty;
        if (lease is not null) await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
    }

    private static async Task<string> CreateFingerprintAsync(string folder, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder($"template-preview-v{PreviewPipelineVersion}|1600");
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            builder.Append('|').Append(Path.GetRelativePath(folder, file))
                .Append(':').Append(info.Length)
                .Append(':').Append(info.LastWriteTimeUtc.Ticks);
            if (string.Equals(info.Name, "config.json", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = File.OpenRead(file);
                builder.Append(':').Append(Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)));
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string Owner(string templateId) => $"template-preview:{templateId}";
}
