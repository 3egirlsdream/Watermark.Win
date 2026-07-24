#nullable enable

using Microsoft.JSInterop;

namespace Watermark.Razor.Workspace;

public sealed record WMSceneSurfaceLease(
    string OwnerKey,
    long OwnerVersion,
    string ResourceKey);

public interface IWMSceneSurfaceTransport : IAsyncDisposable
{
    ValueTask<WMSceneSurfaceLease?> PublishAsync(
        string ownerKey,
        long ownerVersion,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default);
    ValueTask ReleaseAsync(WMSceneSurfaceLease lease);
}

/// <summary>
/// Mobile scene transport. Bytes cross the Hybrid bridge once, are decoded to
/// ImageBitmap in JavaScript, and the temporary Blob becomes immediately
/// collectible. Components receive only an opaque bitmap key.
/// </summary>
public sealed class WMSceneSurfaceTransport(IJSRuntime jsRuntime) : IWMSceneSurfaceTransport
{
    private readonly object gate = new();
    private readonly Dictionary<string, WMSceneSurfaceLease> leases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> requestedGenerations = new(StringComparer.Ordinal);
    private IJSObjectReference? module;
    private long generation;
    private int pendingPublishes;
    private TaskCompletionSource? publishesDrained;
    private bool disposed;

    public async ValueTask<WMSceneSurfaceLease?> PublishAsync(
        string ownerKey,
        long ownerVersion,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentNullException.ThrowIfNull(content);
        long candidateGeneration;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (leases.TryGetValue(ownerKey, out var current)
                && current.OwnerVersion == ownerVersion)
                return current;
            if (current is not null && ownerVersion < current.OwnerVersion)
                return null;

            candidateGeneration = ++generation;
            requestedGenerations[ownerKey] = candidateGeneration;
            pendingPublishes++;
            publishesDrained ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        string? candidateKey = null;
        try
        {
            module ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "./_content/Watermark.Razor/js/mac-template-canvas.js");
            candidateKey = $"wm-scene-{Guid.NewGuid():N}";
            using var streamReference = new DotNetStreamReference(content, leaveOpen: true);
            var decoded = await module.InvokeAsync<bool>(
                "publishSceneBitmap",
                cancellationToken,
                candidateKey,
                streamReference,
                mimeType);
            if (!decoded)
            {
                await ReleaseBitmapAsync(candidateKey);
                return null;
            }

            WMSceneSurfaceLease? previous = null;
            WMSceneSurfaceLease? published = null;
            var candidate = new WMSceneSurfaceLease(ownerKey, ownerVersion, candidateKey);
            lock (gate)
            {
                var isLatestRequest = requestedGenerations.TryGetValue(
                    ownerKey,
                    out var requestedGeneration)
                    && requestedGeneration == candidateGeneration;
                var isNewEnough = !leases.TryGetValue(ownerKey, out var current)
                    || ownerVersion >= current.OwnerVersion;
                if (!disposed && isLatestRequest && isNewEnough)
                {
                    previous = current;
                    published = candidate;
                    leases[ownerKey] = candidate;
                }
            }

            if (published is null)
            {
                await ReleaseBitmapAsync(candidateKey);
                return null;
            }

            if (previous is not null
                && previous.ResourceKey != published.ResourceKey)
                await ReleaseBitmapAsync(previous.ResourceKey);
            return published;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(candidateKey))
                await ReleaseBitmapAsync(candidateKey);
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

    public async ValueTask ReleaseAsync(WMSceneSurfaceLease lease)
    {
        var release = false;
        lock (gate)
        {
            if (leases.TryGetValue(lease.OwnerKey, out var current)
                && current.ResourceKey == lease.ResourceKey)
            {
                leases.Remove(lease.OwnerKey);
                release = true;
            }
        }
        if (release) await ReleaseBitmapAsync(lease.ResourceKey);
    }

    public async ValueTask DisposeAsync()
    {
        WMSceneSurfaceLease[] snapshot;
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
        await waitForPublishes;
        foreach (var lease in snapshot)
            await ReleaseBitmapAsync(lease.ResourceKey);
        if (module is not null)
        {
            try { await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            module = null;
        }
    }

    private async ValueTask ReleaseBitmapAsync(string resourceKey)
    {
        if (module is null) return;
        try { await module.InvokeVoidAsync("releaseSceneBitmap", resourceKey); }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }
}
