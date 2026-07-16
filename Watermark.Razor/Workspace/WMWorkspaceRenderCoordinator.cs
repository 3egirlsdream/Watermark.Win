#nullable enable

namespace Watermark.Razor.Workspace;

/// <summary>
/// Coordinates a single latest-wins preview stream. Flush only waits for the
/// currently queued version and never starts another render.
/// </summary>
public sealed class WMWorkspaceRenderCoordinator : IWMWorkspaceRenderCoordinator, IDisposable
{
    private const int CacheEntryLimit = 32;
    private readonly object gate = new();
    private readonly Dictionary<string, WMWorkspacePreview> cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> cacheAccess = new(StringComparer.Ordinal);
    private long cacheSequence;
    private CancellationTokenSource? currentCancellation;
    private Task<WMWorkspacePreview>? currentRawTask;
    private Task<WMWorkspacePreview>? currentPublishedTask;
    private string? currentCacheKey;
    private long currentVersion;
    private WMWorkspacePreview? currentPreview;
    private bool disposed;

    public event Action<WMWorkspacePreview>? PreviewPublished;

    public WMWorkspacePreviewTicket QueuePreview(
        WMWorkspaceRenderRequest request,
        CancellationToken token = default)
    {
        Task<WMWorkspacePreview> publishTask;
        WMWorkspacePreview? cachedPreview = null;
        var cacheKey = CacheKey(request.SessionId, request.Fingerprint);
        lock (gate)
        {
            ThrowIfDisposed();
            if (request.Version <= currentVersion)
            {
                // Fingerprint preparation is asynchronous, so an older request can
                // legitimately arrive after a newer one. It is stale work, not a
                // caller-facing validation error. Returning a canceled task lets the
                // controller discard it through the normal latest-wins path.
                publishTask = Task.FromCanceled<WMWorkspacePreview>(new CancellationToken(canceled: true));
                return Ticket(request, publishTask);
            }

            currentVersion = request.Version;
            if (cache.TryGetValue(cacheKey, out var cached) && IsValid(cached))
            {
                cacheAccess[cacheKey] = ++cacheSequence;
                currentCancellation?.Cancel();
                currentCancellation?.Dispose();
                currentCancellation = null;
                currentCacheKey = cacheKey;
                cachedPreview = cached with { Version = request.Version, CacheHit = true };
                currentPreview = cachedPreview;
                currentRawTask = Task.FromResult(cachedPreview);
                currentPublishedTask = publishTask = currentRawTask;
            }
            else if (cached is not null)
            {
                cache.Remove(cacheKey);
                cacheAccess.Remove(cacheKey);
                publishTask = StartRenderLocked(request, cacheKey, token);
            }
            else if (string.Equals(currentCacheKey, cacheKey, StringComparison.Ordinal)
                     && currentCancellation is { IsCancellationRequested: false }
                     && currentRawTask is { IsCompleted: false })
            {
                publishTask = PublishWhenCurrentAsync(request.Version, cacheKey, request.Fingerprint, currentRawTask, token);
                currentPublishedTask = publishTask;
            }
            else
            {
                currentCancellation?.Cancel();
                currentCancellation?.Dispose();
                currentCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                currentCacheKey = cacheKey;
                currentRawTask = request.RenderAsync(currentCancellation.Token);
                publishTask = PublishWhenCurrentAsync(
                    request.Version,
                    cacheKey,
                    request.Fingerprint,
                    currentRawTask,
                    currentCancellation.Token);
                currentPublishedTask = publishTask;
            }
        }

        if (cachedPreview is not null) PreviewPublished?.Invoke(cachedPreview);
        return Ticket(request, publishTask);
    }

    public Task<WMWorkspacePreview> FlushAsync(
        WMWorkspacePreviewTicket ticket,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        lock (gate)
        {
            ThrowIfDisposed();
        }
        return ticket.Completion.WaitAsync(token);
    }

    public void CancelPreview()
    {
        lock (gate)
        {
            currentCancellation?.Cancel();
            currentCancellation?.Dispose();
            currentCancellation = null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        CancelPreview();
        disposed = true;
    }

    private async Task<WMWorkspacePreview> PublishWhenCurrentAsync(
        long requestedVersion,
        string cacheKey,
        string fingerprint,
        Task<WMWorkspacePreview> renderTask,
        CancellationToken cancellationToken)
    {
        var rendered = await renderTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        var versioned = rendered with { Version = requestedVersion, Fingerprint = fingerprint };
        var publish = false;
        lock (gate)
        {
            if (!disposed)
            {
                cache[cacheKey] = rendered with { Fingerprint = fingerprint };
                cacheAccess[cacheKey] = ++cacheSequence;
                TrimCacheLocked();
            }
            if (!disposed
                && requestedVersion == currentVersion
                && string.Equals(cacheKey, currentCacheKey, StringComparison.Ordinal))
            {
                currentPreview = versioned;
                publish = true;
            }
        }
        if (publish) PreviewPublished?.Invoke(versioned);
        return versioned;
    }

    private static WMWorkspacePreviewTicket Ticket(
        WMWorkspaceRenderRequest request,
        Task<WMWorkspacePreview> completion) =>
        new(request.SessionId, request.Epoch, request.Version, request.Fingerprint, completion);

    private static string CacheKey(string sessionId, string fingerprint) => $"{sessionId}\n{fingerprint}";

    private Task<WMWorkspacePreview> StartRenderLocked(
        WMWorkspaceRenderRequest request,
        string cacheKey,
        CancellationToken token)
    {
        currentCancellation?.Cancel();
        currentCancellation?.Dispose();
        currentCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        currentCacheKey = cacheKey;
        currentRawTask = request.RenderAsync(currentCancellation.Token);
        currentPublishedTask = PublishWhenCurrentAsync(
            request.Version,
            cacheKey,
            request.Fingerprint,
            currentRawTask,
            currentCancellation.Token);
        return currentPublishedTask;
    }

    private void TrimCacheLocked()
    {
        while (cache.Count > CacheEntryLimit)
        {
            var oldest = cacheAccess.MinBy(pair => pair.Value).Key;
            cache.Remove(oldest);
            cacheAccess.Remove(oldest);
        }
    }

    private static bool IsValid(WMWorkspacePreview preview)
    {
        try { return File.Exists(preview.FilePath) && new FileInfo(preview.FilePath).Length > 0; }
        catch { return false; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
