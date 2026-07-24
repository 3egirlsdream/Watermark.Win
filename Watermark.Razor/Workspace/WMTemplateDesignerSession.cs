using Microsoft.JSInterop;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

#nullable enable

public sealed record WMDesignSceneLayerPresentation(
    WMDesignSceneLayer Layer,
    string? SurfaceUrl);

public sealed record WMDesignScenePresentation(
    WMDesignSceneFrame Frame,
    string BaseUrl,
    IReadOnlyList<WMDesignSceneLayerPresentation> Layers)
{
    public bool UsesInlineFallback =>
        BaseUrl.StartsWith("data:", StringComparison.Ordinal)
        || Layers.Any(layer => layer.SurfaceUrl?.StartsWith("data:", StringComparison.Ordinal) == true);
}

public sealed record WMTemplateDesignerPreview(
    WMDesignRenderResult RenderResult,
    string PreviewUrl,
    long Version)
{
    public WMDesignScenePresentation? Scene { get; init; }
    public bool UsesCompatibilityFallback { get; init; }
}

/// <summary>
/// Owns one cross-platform editor scene, latest-wins scheduling and every
/// browser resource lease. Razor components only consume versioned URLs.
/// </summary>
public sealed class WMTemplateDesignerSession : IAsyncDisposable
{
    private readonly IWMDesignSceneRenderer sceneRenderer;
    private readonly IWMObjectUrlRegistry objectUrls;
    private readonly IWMSceneSurfaceTransport sceneTransport;
    private readonly IWMWorkspacePerformanceCounters? metrics;
    private readonly object gate = new();
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private readonly Dictionary<string, WMObjectUrlLease> sceneLeases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WMSceneSurfaceLease> bitmapLeases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> inlineUrls = new(StringComparer.Ordinal);
    private CancellationTokenSource? renderCancellation;
    private IWMDesignSceneSession? sceneSession;
    private bool compatibilityMode;
    private long version;
    private bool disposed;

    public WMTemplateDesignerSession(
        IWMDesignSceneRenderer sceneRenderer,
        IWMObjectUrlRegistry objectUrls,
        IWMSceneSurfaceTransport sceneTransport,
        IWMWorkspacePerformanceCounters metrics)
        : this(sceneRenderer, objectUrls, sceneTransport)
    {
        this.metrics = metrics;
    }

    public WMTemplateDesignerSession(
        IWMDesignSceneRenderer sceneRenderer,
        IWMObjectUrlRegistry objectUrls,
        IWMSceneSurfaceTransport sceneTransport)
    {
        this.sceneRenderer = sceneRenderer;
        this.objectUrls = objectUrls;
        this.sceneTransport = sceneTransport;
    }

    public WMTemplateDesignerSession(
        IWMDesignSceneRenderer sceneRenderer,
        IWMObjectUrlRegistry objectUrls)
        : this(sceneRenderer, objectUrls, new WMNullSceneSurfaceTransport())
    {
    }

    /// <summary>Compatibility constructor used by hosts/tests with only the old renderer.</summary>
    public WMTemplateDesignerSession(
        IWMWatermarkHelper watermarkHelper,
        IWMObjectUrlRegistry objectUrls)
        : this(
            new WMCompatibilityDesignSceneRenderer(watermarkHelper),
            objectUrls,
            new WMNullSceneSurfaceTransport())
    {
    }

    public Task<WMTemplateDesignerPreview?> RenderSceneAsync(
        string ownerKey,
        WMCanvas canvas,
        WMTemplateChangeSet changeSet,
        WMDesignSceneQuality quality,
        bool publishObjectUrl = true,
        long? surfaceCacheBudgetBytes = null,
        CancellationToken cancellationToken = default) =>
        RunLatestAsync(
            ownerKey,
            canvas,
            publishObjectUrl,
            async (snapshot, token) =>
            {
                if (compatibilityMode
                    && sceneRenderer is IWMDesignSceneFallbackRenderer fallbackRenderer)
                {
                    var fallback = await fallbackRenderer.RenderCompatibilityAsync(
                        snapshot,
                        null,
                        token);
                    await ReleaseRetiredSurfacesAsync(ownerKey, new HashSet<string>(StringComparer.Ordinal));
                    return await PublishFlattenedAsync(
                        ownerKey,
                        fallback,
                        publishObjectUrl,
                        token,
                        usesCompatibilityFallback: true);
                }

                WMDesignSceneUpdate update;
                var beforeMetrics = sceneSession?.Metrics;
                try
                {
                    if (sceneSession is null)
                    {
                        sceneSession = sceneRenderer is IWMDesignSceneRendererWithOptions configurable
                            && surfaceCacheBudgetBytes is > 0
                                ? await configurable.OpenSessionAsync(
                                    snapshot,
                                    null,
                                    new WMDesignSceneSessionOptions(surfaceCacheBudgetBytes.Value),
                                    token)
                                : await sceneRenderer.OpenSessionAsync(snapshot, null, token);
                        update = new WMDesignSceneUpdate(
                            sceneSession.CurrentFrame,
                            WMDesignScenePatch.Initial(sceneSession.CurrentFrame),
                            false,
                            sceneSession.CurrentFrame.Layers.Count(layer => layer.HasSurface));
                    }
                    else
                    {
                        update = await sceneSession.UpdateAsync(snapshot, changeSet, quality, token);
                    }
                }
                catch (Exception ex) when (
                    ex is not OperationCanceledException
                    && sceneRenderer is IWMDesignSceneFallbackRenderer)
                {
                    if (sceneSession is not null)
                    {
                        await sceneSession.DisposeAsync();
                        sceneSession = null;
                    }
                    compatibilityMode = true;
                    await ReleaseRetiredSurfacesAsync(ownerKey, new HashSet<string>(StringComparer.Ordinal));
                    var fallback = await ((IWMDesignSceneFallbackRenderer)sceneRenderer)
                        .RenderCompatibilityAsync(snapshot, null, token);
                    return await PublishFlattenedAsync(
                        ownerKey,
                        fallback,
                        publishObjectUrl,
                        token,
                        usesCompatibilityFallback: true);
                }
                var afterMetrics = sceneSession.Metrics;
                RecordSceneMetrics(beforeMetrics, afterMetrics, update.RasterizedLayerCount);
                metrics?.Increment(update.CacheHit
                    ? WMWorkspaceMetricStage.LayerCacheHit
                    : WMWorkspaceMetricStage.LayerCacheMiss);

                var renderResult = update.Frame.ToRenderResult();
                return await PublishSceneAsync(
                    ownerKey,
                    renderResult,
                    update.Frame,
                    publishObjectUrl,
                    token);
            },
            cancellationToken);

    /// <summary>
    /// Compatibility entry. It updates the shared scene and then flushes the
    /// same latest revision; there is no separate preview renderer.
    /// </summary>
    public Task<WMTemplateDesignerPreview?> RenderPreviewAsync(
        string ownerKey,
        WMCanvas canvas,
        bool publishObjectUrl = true,
        CancellationToken cancellationToken = default) =>
        RunLatestAsync(
            ownerKey,
            canvas,
            publishObjectUrl,
            async (snapshot, token) =>
            {
                var beforeMetrics = sceneSession?.Metrics;
                if (sceneSession is null)
                {
                    sceneSession = await sceneRenderer.OpenSessionAsync(snapshot, null, token);
                }
                else
                {
                    await sceneSession.UpdateAsync(
                        snapshot,
                        WMTemplateChangeSet.Initial(version),
                        WMDesignSceneQuality.Exact,
                        token);
                }

                var result = await sceneSession.FlushAsync(token);
                RecordSceneMetrics(beforeMetrics, sceneSession.Metrics);
                return await PublishFlattenedAsync(
                    ownerKey,
                    result,
                    publishObjectUrl,
                    token);
            },
            cancellationToken);

    public async Task<WMTemplateDesignerPreview?> FlushAsync(
        string ownerKey,
        bool publishObjectUrl = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        await renderGate.WaitAsync(cancellationToken);
        try
        {
            if (sceneSession is null) return null;
            var beforeMetrics = sceneSession.Metrics;
            var result = await sceneSession.FlushAsync(cancellationToken);
            RecordSceneMetrics(beforeMetrics, sceneSession.Metrics);
            return await PublishFlattenedAsync(
                ownerKey,
                result,
                publishObjectUrl,
                cancellationToken);
        }
        finally
        {
            renderGate.Release();
        }
    }

    public async Task<WMDesignRenderResult?> FlushRenderAsync(
        CancellationToken cancellationToken = default)
    {
        await renderGate.WaitAsync(cancellationToken);
        try
        {
            if (sceneSession is null) return null;
            var beforeMetrics = sceneSession.Metrics;
            var result = await sceneSession.FlushAsync(cancellationToken);
            RecordSceneMetrics(beforeMetrics, sceneSession.Metrics);
            return result;
        }
        finally
        {
            renderGate.Release();
        }
    }

    private void RecordSceneMetrics(
        WMDesignSceneMetricsSnapshot? before,
        WMDesignSceneMetricsSnapshot after,
        int minimumRasterCount = 0)
    {
        metrics?.Record(
            WMWorkspaceMetricStage.SceneLayout,
            after.LayoutCount - (before?.LayoutCount ?? 0),
            after.LayoutElapsedMilliseconds
                - (before?.LayoutElapsedMilliseconds ?? 0));
        metrics?.Record(
            WMWorkspaceMetricStage.LayerRaster,
            Math.Max(
                minimumRasterCount,
                after.LayerRasterCount - (before?.LayerRasterCount ?? 0)),
            after.LayerRasterElapsedMilliseconds
                - (before?.LayerRasterElapsedMilliseconds ?? 0));
        metrics?.Record(
            WMWorkspaceMetricStage.SceneComposite,
            after.CompositeCount - (before?.CompositeCount ?? 0),
            after.CompositeElapsedMilliseconds
                - (before?.CompositeElapsedMilliseconds ?? 0));
        metrics?.Record(
            WMWorkspaceMetricStage.Encode,
            after.EncodeCount - (before?.EncodeCount ?? 0),
            after.EncodeElapsedMilliseconds
                - (before?.EncodeElapsedMilliseconds ?? 0));
    }

    private async Task<WMTemplateDesignerPreview?> RunLatestAsync(
        string ownerKey,
        WMCanvas canvas,
        bool publishObjectUrl,
        Func<WMCanvas, CancellationToken, Task<WMTemplateDesignerPreview?>> render,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentNullException.ThrowIfNull(canvas);

        CancellationTokenSource current;
        CancellationTokenSource? previousCancellation;
        long currentVersion;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            previousCancellation = renderCancellation;
            current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            renderCancellation = current;
            currentVersion = ++version;
        }
        previousCancellation?.Cancel();

        var enteredRenderGate = false;
        try
        {
            var snapshot = CloneCanvas(canvas);
            await renderGate.WaitAsync(current.Token);
            enteredRenderGate = true;
            current.Token.ThrowIfCancellationRequested();
            var preview = await render(snapshot, current.Token);
            if (!IsCurrent(current, currentVersion)) return null;
            return preview is null ? null : preview with { Version = currentVersion };
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (enteredRenderGate) renderGate.Release();
            lock (gate)
            {
                if (ReferenceEquals(renderCancellation, current))
                    renderCancellation = null;
            }
            current.Dispose();
        }
    }

    private async Task<WMTemplateDesignerPreview?> PublishSceneAsync(
        string ownerKey,
        WMDesignRenderResult renderResult,
        WMDesignSceneFrame frame,
        bool publishObjectUrl,
        CancellationToken cancellationToken)
    {
        var baseUrl = await PublishSurfaceAsync(
            $"{ownerKey}:base",
            frame.BaseSurfaceVersion,
            frame.BaseImageBytes,
            publishObjectUrl,
            cancellationToken);
        if (baseUrl is null) return null;

        var liveOwners = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{ownerKey}:base"
        };
        var layers = new List<WMDesignSceneLayerPresentation>(frame.Layers.Count);
        foreach (var layer in frame.Layers)
        {
            string? surfaceUrl = null;
            if (layer.HasSurface)
            {
                var layerOwner = $"{ownerKey}:layer:{layer.NodeId}";
                liveOwners.Add(layerOwner);
                surfaceUrl = await PublishSurfaceAsync(
                    layerOwner,
                    layer.SurfaceVersion,
                    layer.SurfaceBytes!,
                    publishObjectUrl,
                    cancellationToken);
                if (surfaceUrl is null) return null;
            }
            layers.Add(new WMDesignSceneLayerPresentation(layer, surfaceUrl));
        }
        await ReleaseRetiredSurfacesAsync(ownerKey, liveOwners);

        var scene = new WMDesignScenePresentation(frame, baseUrl, layers);
        return new WMTemplateDesignerPreview(renderResult, baseUrl, version)
        {
            Scene = scene
        };
    }

    private async Task<WMTemplateDesignerPreview?> PublishFlattenedAsync(
        string ownerKey,
        WMDesignRenderResult result,
        bool publishObjectUrl,
        CancellationToken cancellationToken,
        bool usesCompatibilityFallback = false)
    {
        if (result.ImageBytes.Length == 0)
            throw new InvalidOperationException("模板场景 Flush 没有生成权威预览。");
        var url = await PublishSurfaceAsync(
            $"{ownerKey}:flattened",
            version,
            result.ImageBytes,
            publishObjectUrl,
            cancellationToken,
            "image/jpeg");
        return url is null
            ? null
            : new WMTemplateDesignerPreview(result, url, version)
            {
                UsesCompatibilityFallback = usesCompatibilityFallback
            };
    }

    private async Task<string?> PublishSurfaceAsync(
        string surfaceOwner,
        long surfaceVersion,
        byte[] bytes,
        bool publishObjectUrl,
        CancellationToken cancellationToken,
        string? mimeTypeOverride = null)
    {
        if (bytes.Length == 0) return null;
        using var uploadMeasurement = metrics?.Measure(WMWorkspaceMetricStage.LayerUpload);
        if (!publishObjectUrl)
        {
            if (sceneLeases.Remove(surfaceOwner, out var oldLease))
                await objectUrls.ReleaseAsync(oldLease);
            if (bitmapLeases.TryGetValue(surfaceOwner, out var currentBitmap)
                && currentBitmap.OwnerVersion == surfaceVersion)
                return $"wmbitmap:{currentBitmap.ResourceKey}";

            try
            {
                await using var bitmapContent = new MemoryStream(bytes, writable: false);
                var bitmap = await sceneTransport.PublishAsync(
                    surfaceOwner,
                    surfaceVersion,
                    bitmapContent,
                    mimeTypeOverride ?? WMTemplatePreviewSource.DetectMimeType(bytes),
                    cancellationToken);
                if (bitmap is not null)
                {
                    bitmapLeases[surfaceOwner] = bitmap;
                    inlineUrls.Remove(surfaceOwner);
                    return $"wmbitmap:{bitmap.ResourceKey}";
                }
            }
            catch (JSException)
            {
            }
            catch (NotSupportedException)
            {
            }

            if (bitmapLeases.Remove(surfaceOwner, out var fallbackBitmap))
                await sceneTransport.ReleaseAsync(fallbackBitmap);
            var inline = WMTemplatePreviewSource.CreateInlineDataUrl(bytes);
            inlineUrls[surfaceOwner] = inline;
            return inline;
        }

        inlineUrls.Remove(surfaceOwner);
        if (bitmapLeases.Remove(surfaceOwner, out var oldBitmap))
            await sceneTransport.ReleaseAsync(oldBitmap);
        if (sceneLeases.TryGetValue(surfaceOwner, out var current)
            && current.OwnerVersion == surfaceVersion)
            return current.Url;

        await using var content = new MemoryStream(bytes, writable: false);
        var next = await objectUrls.PublishAsync(
            surfaceOwner,
            surfaceVersion,
            content,
            mimeTypeOverride ?? WMTemplatePreviewSource.DetectMimeType(bytes),
            cancellationToken);
        if (next is null) return null;
        sceneLeases[surfaceOwner] = next;
        return next.Url;
    }

    private async Task ReleaseRetiredSurfacesAsync(
        string ownerKey,
        IReadOnlySet<string> liveOwners)
    {
        var prefix = $"{ownerKey}:";
        var retired = sceneLeases
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)
                && pair.Key != $"{ownerKey}:flattened"
                && !liveOwners.Contains(pair.Key))
            .ToArray();
        foreach (var pair in retired)
        {
            sceneLeases.Remove(pair.Key);
            await objectUrls.ReleaseAsync(pair.Value);
        }
        var retiredBitmaps = bitmapLeases
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)
                && pair.Key != $"{ownerKey}:flattened"
                && !liveOwners.Contains(pair.Key))
            .ToArray();
        foreach (var pair in retiredBitmaps)
        {
            bitmapLeases.Remove(pair.Key);
            await sceneTransport.ReleaseAsync(pair.Value);
        }
        foreach (var key in inlineUrls.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal)
                && key != $"{ownerKey}:flattened"
                && !liveOwners.Contains(key))
            .ToArray())
            inlineUrls.Remove(key);
    }

    public async ValueTask ClearAsync()
    {
        CancellationTokenSource? cancellation;
        lock (gate)
        {
            version++;
            cancellation = renderCancellation;
            renderCancellation = null;
        }
        cancellation?.Cancel();

        await renderGate.WaitAsync();
        try
        {
            if (sceneSession is not null)
            {
                await sceneSession.DisposeAsync();
                sceneSession = null;
            }
            compatibilityMode = false;
            var leases = sceneLeases.Values.ToArray();
            var bitmaps = bitmapLeases.Values.ToArray();
            sceneLeases.Clear();
            bitmapLeases.Clear();
            inlineUrls.Clear();
            foreach (var lease in leases)
                await objectUrls.ReleaseAsync(lease);
            foreach (var bitmap in bitmaps)
                await sceneTransport.ReleaseAsync(bitmap);
        }
        finally
        {
            renderGate.Release();
        }
    }

    private bool IsCurrent(CancellationTokenSource source, long candidateVersion)
    {
        lock (gate)
        {
            return !disposed
                && !source.IsCancellationRequested
                && ReferenceEquals(renderCancellation, source)
                && version == candidateVersion;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        await ClearAsync();
        renderGate.Dispose();
    }

    private static WMCanvas CloneCanvas(WMCanvas source)
    {
        var clone = Global.ReadConfig(Global.CanvasSerialize(source));
        clone.Path = source.Path;
        clone.Exif = source.Exif;
        return clone;
    }
}

internal sealed class WMNullSceneSurfaceTransport : IWMSceneSurfaceTransport
{
    public ValueTask<WMSceneSurfaceLease?> PublishAsync(
        string ownerKey,
        long ownerVersion,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<WMSceneSurfaceLease?>(null);

    public ValueTask ReleaseAsync(WMSceneSurfaceLease lease) =>
        ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class WMCompatibilityDesignSceneRenderer(
    IWMWatermarkHelper helper) : IWMDesignSceneFallbackRenderer
{
    public Task<WMDesignRenderResult> RenderCompatibilityAsync(
        WMCanvas canvas,
        WMZipedTemplate? ziped = null,
        CancellationToken cancellationToken = default) =>
        helper.GenerationDesignPreviewAsync(canvas, ziped, cancellationToken);

    public async ValueTask<IWMDesignSceneSession> OpenSessionAsync(
        WMCanvas canvas,
        WMZipedTemplate? ziped = null,
        CancellationToken cancellationToken = default)
    {
        var result = await helper.GenerationDesignPreviewAsync(canvas, ziped, cancellationToken);
        return new CompatibilitySession(helper, ziped, result);
    }

    private sealed class CompatibilitySession(
        IWMWatermarkHelper helper,
        WMZipedTemplate? ziped,
        WMDesignRenderResult initial) : IWMDesignSceneSession
    {
        private WMDesignRenderResult result = initial;

        public WMDesignSceneFrame CurrentFrame { get; private set; } = ToFrame(initial, 1);
        public WMDesignSceneMetricsSnapshot Metrics { get; private set; } =
            new(1, 1, 1, 1, 1, 0, 1);

        public async Task<WMDesignSceneUpdate> UpdateAsync(
            WMCanvas canvas,
            WMTemplateChangeSet changeSet,
            WMDesignSceneQuality quality,
            CancellationToken cancellationToken = default)
        {
            result = await helper.GenerationDesignPreviewAsync(canvas, ziped, cancellationToken);
            CurrentFrame = ToFrame(result, Math.Max(CurrentFrame.Revision + 1, changeSet.Revision));
            Metrics = Metrics with
            {
                DecodeCount = Metrics.DecodeCount + 1,
                LayoutCount = Metrics.LayoutCount + 1,
                LayerRasterCount = Metrics.LayerRasterCount + 1,
                CompositeCount = Metrics.CompositeCount + 1,
                EncodeCount = Metrics.EncodeCount + 1,
                CacheMissCount = Metrics.CacheMissCount + 1
            };
            return new WMDesignSceneUpdate(
                CurrentFrame,
                WMDesignScenePatch.Initial(CurrentFrame),
                false,
                1);
        }

        public Task<WMDesignRenderResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result with { Scene = CurrentFrame });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static WMDesignSceneFrame ToFrame(WMDesignRenderResult result, long revision) =>
            new(
                revision,
                result.CanvasWidth,
                result.CanvasHeight,
                result.ContentViewport,
                result.Layout,
                $"compat:{revision}",
                revision,
                result.ImageBytes,
                [],
                true);
    }
}
