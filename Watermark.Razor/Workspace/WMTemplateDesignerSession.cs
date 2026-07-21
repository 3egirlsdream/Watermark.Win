using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

#nullable enable

public sealed record WMTemplateDesignerPreview(
    WMDesignRenderResult RenderResult,
    string PreviewUrl,
    long Version);

/// <summary>
/// Owns the non-visual preview lifecycle shared by the mobile and desktop
/// template designers. The visual components keep their own DOM and CSS, while
/// rendering, latest-wins cancellation, and Blob URL ownership remain here.
/// </summary>
public sealed class WMTemplateDesignerSession(
    IWMWatermarkHelper watermarkHelper,
    IWMObjectUrlRegistry objectUrls) : IAsyncDisposable
{
    private const string PreviewMimeType = "image/jpeg";

    private readonly object gate = new();
    // GenerationDesignPreviewAsync cannot interrupt an encode that is already
    // inside Skia. Keep that unavoidable render single-flight: a newer request
    // cancels every waiter, then starts as soon as the current render unwinds.
    // This prevents a slider/drag gesture from launching many obsolete renders
    // in parallel while preserving latest-wins publication.
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private CancellationTokenSource? renderCancellation;
    private WMObjectUrlLease? previewLease;
    private long version;
    private bool disposed;

    public async Task<WMTemplateDesignerPreview?> RenderPreviewAsync(
        string ownerKey,
        WMCanvas canvas,
        bool publishObjectUrl = true,
        CancellationToken cancellationToken = default)
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
            // Capture before the first await so this request is immutable even
            // if the editor continues changing while another render unwinds.
            var snapshot = WMTemplateEditorState.Create(canvas).Draft;
            await renderGate.WaitAsync(current.Token).ConfigureAwait(false);
            enteredRenderGate = true;
            current.Token.ThrowIfCancellationRequested();
            var result = await watermarkHelper.GenerationDesignPreviewAsync(
                snapshot,
                null,
                current.Token).ConfigureAwait(false);
            if (!IsCurrent(current, currentVersion)) return null;

            if (!publishObjectUrl)
            {
                // Mobile WKWebView can keep painting an old blob-backed image
                // without raising an error event. Publish the same encoded
                // bytes inline on mobile; this does not decode, render, or
                // encode again and avoids creating an unused Blob URL.
                var inlineUrl = WMTemplatePreviewSource.CreateInlineDataUrl(result.ImageBytes);
                if (!IsCurrent(current, currentVersion)) return null;

                WMObjectUrlLease? retiredLease;
                lock (gate)
                {
                    if (!IsCurrentUnsafe(current, currentVersion)) return null;
                    retiredLease = previewLease;
                    previewLease = null;
                }
                if (retiredLease is not null)
                    await objectUrls.ReleaseAsync(retiredLease).ConfigureAwait(false);

                return new WMTemplateDesignerPreview(result, inlineUrl, currentVersion);
            }

            await using var content = new MemoryStream(result.ImageBytes, writable: false);
            var nextLease = await objectUrls.PublishAsync(
                ownerKey,
                currentVersion,
                content,
                PreviewMimeType,
                current.Token).ConfigureAwait(false);
            if (nextLease is null) return null;
            if (!IsCurrent(current, currentVersion))
            {
                await objectUrls.ReleaseAsync(nextLease).ConfigureAwait(false);
                return null;
            }

            WMObjectUrlLease? previousLease;
            lock (gate)
            {
                if (!IsCurrentUnsafe(current, currentVersion))
                {
                    previousLease = null;
                }
                else
                {
                    previousLease = previewLease;
                    previewLease = nextLease;
                }
            }
            if (!ReferenceEquals(previewLease, nextLease))
            {
                await objectUrls.ReleaseAsync(nextLease).ConfigureAwait(false);
                return null;
            }
            if (previousLease is not null)
                await objectUrls.ReleaseAsync(previousLease).ConfigureAwait(false);

            return new WMTemplateDesignerPreview(result, nextLease.Url, currentVersion);
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

    public async ValueTask ClearAsync()
    {
        CancellationTokenSource? cancellation;
        WMObjectUrlLease? lease;
        lock (gate)
        {
            version++;
            cancellation = renderCancellation;
            renderCancellation = null;
            lease = previewLease;
            previewLease = null;
        }
        cancellation?.Cancel();
        if (lease is not null)
            await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
    }

    private bool IsCurrent(CancellationTokenSource source, long candidateVersion)
    {
        lock (gate) return IsCurrentUnsafe(source, candidateVersion);
    }

    private bool IsCurrentUnsafe(CancellationTokenSource source, long candidateVersion) =>
        !disposed
        && !source.IsCancellationRequested
        && ReferenceEquals(renderCancellation, source)
        && version == candidateVersion;

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        await ClearAsync().ConfigureAwait(false);
    }
}
