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
    private CancellationTokenSource? renderCancellation;
    private WMObjectUrlLease? previewLease;
    private long version;
    private bool disposed;

    public async Task<WMTemplateDesignerPreview?> RenderPreviewAsync(
        string ownerKey,
        WMCanvas canvas,
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

        try
        {
            var snapshot = WMTemplateEditorState.Create(canvas).Draft;
            var result = await watermarkHelper.GenerationDesignPreviewAsync(
                snapshot,
                null,
                current.Token).ConfigureAwait(false);
            if (!IsCurrent(current, currentVersion)) return null;

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
