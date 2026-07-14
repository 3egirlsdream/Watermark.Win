using Microsoft.JSInterop;
using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac;

public sealed class MacTemplateLibraryService : IAsyncDisposable
{
    private readonly IWMWatermarkHelper helper;
    private readonly IJSRuntime jsRuntime;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly Dictionary<string, MacTemplateLibraryEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> invalidatedTemplateIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> previewQueue = new();
    private readonly HashSet<string> queuedPreviewKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object previewQueueLock = new();
    private bool previewWorkerRunning;

    public MacTemplateLibraryService(IWMWatermarkHelper helper, IJSRuntime jsRuntime)
    {
        this.helper = helper;
        this.jsRuntime = jsRuntime;
    }

    public event Action Changed = delegate { };

    public IReadOnlyList<WMTemplateList> Templates => entries.Values
        .Where(x => x.Canvas != null)
        .OrderBy(x => x.Canvas.CanvasType)
        .ThenBy(x => x.Canvas.Name)
        .Select(x => x.ToTemplateList())
        .ToList();

    public async Task<IReadOnlyList<WMTemplateList>> GetOrRefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (force)
        {
            await ForceReloadAsync(cancellationToken);
        }
        else
        {
            await RefreshChangedAsync(cancellationToken);
        }

        return Templates;
    }

    public async Task ForceReloadAsync(CancellationToken cancellationToken = default)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            ClearPreviewQueue();
            foreach (var entry in entries.Values)
            {
                await RevokePreviewAsync(entry);
            }

            entries.Clear();
            invalidatedTemplateIds.Clear();
            await RefreshChangedCoreAsync(cancellationToken);
        }
        finally
        {
            refreshLock.Release();
        }

        StartPreviewWorker();
    }

    public async Task RefreshChangedAsync(CancellationToken cancellationToken = default)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            await RefreshChangedCoreAsync(cancellationToken);
        }
        finally
        {
            refreshLock.Release();
        }

        StartPreviewWorker();
    }

    public void Invalidate(string templateId)
    {
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            invalidatedTemplateIds.Add(templateId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        ClearPreviewQueue();
        foreach (var entry in entries.Values)
        {
            await RevokePreviewAsync(entry);
        }

        refreshLock.Dispose();
    }

    private async Task RefreshChangedCoreAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Global.AppPath.TemplatesFolder))
        {
            Directory.CreateDirectory(Global.AppPath.TemplatesFolder);
        }

        var folders = new DirectoryInfo(Global.AppPath.TemplatesFolder)
            .GetDirectories()
            .Where(x => File.Exists(Path.Combine(x.FullName, "config.json")))
            .ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var removedId in entries.Keys.Except(folders.Keys, StringComparer.OrdinalIgnoreCase).ToList())
        {
            await RevokePreviewAsync(entries[removedId]);
            entries.Remove(removedId);
        }

        foreach (var folder in folders.Values.OrderBy(x => x.Name))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var configPath = Path.Combine(folder.FullName, "config.json");
            var timestamp = File.GetLastWriteTimeUtc(configPath);
            entries.TryGetValue(folder.Name, out var existing);
            var isInvalidated = invalidatedTemplateIds.Contains(folder.Name)
                                || (existing != null && invalidatedTemplateIds.Contains(existing.TemplateId));
            var mustReload = existing == null
                             || existing.ConfigLastWriteTimeUtc != timestamp
                             || isInvalidated;

            if (!mustReload)
            {
                continue;
            }

            if (existing != null)
            {
                await RevokePreviewAsync(existing);
            }

            try
            {
                var canvas = await Task.Run(() => Global.ReadConfigFromPath(configPath), cancellationToken);
                canvas.Exif[canvas.ID] = ExifHelper.DefaultMeta;
                await Global.InitFonts([canvas]);

                var entry = new MacTemplateLibraryEntry
                {
                    TemplateId = canvas.ID,
                    FolderPath = folder.FullName,
                    ConfigPath = configPath,
                    ConfigLastWriteTimeUtc = timestamp,
                    Canvas = canvas,
                    LoadState = MacTemplateLoadState.PreviewPending
                };

                entries[folder.Name] = entry;
                invalidatedTemplateIds.Remove(folder.Name);
                invalidatedTemplateIds.Remove(canvas.ID);
                QueuePreview(folder.Name);
                Changed.Invoke();
            }
            catch (Exception ex)
            {
                if (existing != null)
                {
                    existing.LoadState = MacTemplateLoadState.Error;
                    existing.ErrorMessage = ex.Message;
                }
            }
        }

        Changed.Invoke();
    }

    private void QueuePreview(string key)
    {
        lock (previewQueueLock)
        {
            if (!queuedPreviewKeys.Add(key))
            {
                return;
            }

            previewQueue.Enqueue(key);
        }
    }

    private void ClearPreviewQueue()
    {
        lock (previewQueueLock)
        {
            previewQueue.Clear();
            queuedPreviewKeys.Clear();
        }
    }

    private void StartPreviewWorker()
    {
        lock (previewQueueLock)
        {
            if (previewWorkerRunning || previewQueue.Count == 0)
            {
                return;
            }

            previewWorkerRunning = true;
        }

        _ = ProcessPreviewQueueAsync();
    }

    private async Task ProcessPreviewQueueAsync()
    {
        while (true)
        {
            string key;
            lock (previewQueueLock)
            {
                if (previewQueue.Count == 0)
                {
                    previewWorkerRunning = false;
                    return;
                }

                key = previewQueue.Dequeue();
                queuedPreviewKeys.Remove(key);
            }

            MacTemplateLibraryEntry entry;
            await refreshLock.WaitAsync();
            try
            {
                entries.TryGetValue(key, out entry);
            }
            finally
            {
                refreshLock.Release();
            }

            if (entry == null || entry.LoadState != MacTemplateLoadState.PreviewPending)
            {
                continue;
            }

            await GeneratePreviewAsync(key, entry);
        }
    }

    private async Task GeneratePreviewAsync(string key, MacTemplateLibraryEntry entry)
    {
        var previewSrc = string.Empty;
        var errorMessage = string.Empty;

        try
        {
#pragma warning disable CS8625
            var bytes = await helper.GenerationAsync(entry.Canvas, null, true, designMode: true);
#pragma warning restore CS8625
            if (bytes.Length == 0)
                throw new InvalidOperationException("模板预览生成结果为空。");
            previewSrc = await Global.Byte2Url(jsRuntime, bytes);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }

        await refreshLock.WaitAsync();
        try
        {
            if (!entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry))
            {
                if (!string.IsNullOrWhiteSpace(previewSrc))
                {
                    await Global.RevokeUrl(jsRuntime, previewSrc);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(previewSrc))
            {
                current.PreviewSrc = previewSrc;
                current.PreviewGeneratedAt = DateTime.UtcNow;
                current.LoadState = MacTemplateLoadState.Ready;
                current.ErrorMessage = string.Empty;
            }
            else
            {
                current.LoadState = MacTemplateLoadState.Error;
                current.ErrorMessage = errorMessage;
            }
        }
        finally
        {
            refreshLock.Release();
            Changed.Invoke();
        }
    }

    private async Task RevokePreviewAsync(MacTemplateLibraryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.PreviewSrc))
        {
            await Global.RevokeUrl(jsRuntime, entry.PreviewSrc);
            entry.PreviewSrc = string.Empty;
        }
    }
}
