#nullable enable

using Microsoft.JSInterop;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Owns every Blob URL created by a workspace scope. Publishing is a
/// generation-aware compare-and-swap: a late producer may revoke only its own
/// candidate and can never replace or release a newer owner generation.
/// </summary>
public sealed class WMObjectUrlRegistry(
    IJSRuntime jsRuntime,
    IWMWorkspacePerformanceCounters metrics) : IWMObjectUrlRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<string, WMObjectUrlLease> leases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> requestedGenerations = new(StringComparer.Ordinal);
    private long generation;
    private int pendingPublishes;
    private TaskCompletionSource? publishesDrained;
    private bool disposed;

    public int ActiveLeaseCount
    {
        get
        {
            lock (gate) return leases.Count;
        }
    }

    public async ValueTask<WMObjectUrlLease?> PublishAsync(
        string ownerKey,
        long ownerVersion,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead) throw new ArgumentException("Blob 内容流不可读。", nameof(content));
        if (ownerVersion < 0) throw new ArgumentOutOfRangeException(nameof(ownerVersion));

        long candidateGeneration;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (leases.TryGetValue(ownerKey, out var current) && ownerVersion < current.OwnerVersion)
                return null;

            candidateGeneration = ++generation;
            requestedGenerations[ownerKey] = candidateGeneration;
            pendingPublishes++;
            publishesDrained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        string? candidateUrl = null;
        try
        {
            using (metrics.Measure(WMWorkspaceMetricStage.BlobCreate))
            using (var streamReference = new DotNetStreamReference(content, leaveOpen: true))
            {
                candidateUrl = await jsRuntime.InvokeAsync<string>(
                    "watermarkObjectUrls.createFromStream",
                    cancellationToken,
                    streamReference,
                    string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
            }

            WMObjectUrlLease? previous = null;
            WMObjectUrlLease? published = null;
            lock (gate)
            {
                var isLatestRequest = requestedGenerations.TryGetValue(ownerKey, out var requested)
                                      && requested == candidateGeneration;
                var isNewEnough = !leases.TryGetValue(ownerKey, out var current)
                                  || ownerVersion >= current.OwnerVersion;
                if (!disposed && isLatestRequest && isNewEnough)
                {
                    previous = current;
                    published = new WMObjectUrlLease(ownerKey, ownerVersion, candidateUrl, candidateGeneration);
                    leases[ownerKey] = published;
                }
            }

            if (published is null)
            {
                await RevokeAsync(candidateUrl).ConfigureAwait(false);
                return null;
            }

            if (previous is not null && previous.Generation != published.Generation)
                await RevokeAsync(previous.Url).ConfigureAwait(false);
            return published;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(candidateUrl))
                await RevokeAsync(candidateUrl).ConfigureAwait(false);
            throw;
        }
        finally
        {
            TaskCompletionSource? drained = null;
            lock (gate)
            {
                pendingPublishes--;
                if (pendingPublishes == 0)
                {
                    drained = publishesDrained;
                    publishesDrained = null;
                }
            }
            drained?.TrySetResult();
        }
    }

    public async ValueTask ReleaseAsync(WMObjectUrlLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var shouldRevoke = false;
        lock (gate)
        {
            if (leases.TryGetValue(lease.OwnerKey, out var current)
                && current.Generation == lease.Generation)
            {
                leases.Remove(lease.OwnerKey);
                shouldRevoke = true;
            }
        }
        if (shouldRevoke) await RevokeAsync(lease.Url).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        WMObjectUrlLease[] snapshot;
        Task waitForPublishes;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            snapshot = leases.Values.ToArray();
            leases.Clear();
            requestedGenerations.Clear();
            waitForPublishes = pendingPublishes == 0
                ? Task.CompletedTask
                : publishesDrained?.Task ?? Task.CompletedTask;
        }

        await waitForPublishes.ConfigureAwait(false);
        foreach (var lease in snapshot)
            await RevokeAsync(lease.Url).ConfigureAwait(false);
    }

    private async Task RevokeAsync(string url)
    {
        try { await jsRuntime.InvokeVoidAsync("watermarkObjectUrls.revoke", url); }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }
}
