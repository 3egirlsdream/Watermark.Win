using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMTemplateDesignerSessionTests
{
    [Theory]
    [InlineData(new byte[] { 0xff, 0xd8, 0xff, 0x01 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, "image/png")]
    public void InlineFallback_ReusesEncodedPreviewBytes(byte[] bytes, string mimeType)
    {
        var source = WMTemplatePreviewSource.CreateInlineDataUrl(bytes);

        var prefix = $"data:{mimeType};base64,";
        Assert.StartsWith(prefix, source, StringComparison.Ordinal);
        Assert.Equal(bytes, Convert.FromBase64String(source[prefix.Length..]));
    }

    [Fact]
    public async Task RenderPreviewAsync_CancelsOlderRenderAndPublishesOnlyLatest()
    {
        var helper = new LatestWinsWatermarkHelper();
        var urls = new FakeObjectUrlRegistry();
        await using var session = new WMTemplateDesignerSession(helper, urls);

        var first = session.RenderPreviewAsync("designer", new WMCanvas { Name = "first" });
        await helper.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = await session.RenderPreviewAsync("designer", new WMCanvas { Name = "second" });
        var stale = await first;

        Assert.Null(stale);
        Assert.NotNull(second);
        Assert.Equal("blob:2", second.PreviewUrl);
        Assert.Equal(2, helper.CallCount);
        Assert.Equal([2L], urls.PublishedVersions);
        Assert.Equal(["image/jpeg"], urls.PublishedMimeTypes);

        await session.ClearAsync();
        Assert.Equal([2L], urls.ReleasedVersions);
    }

    private sealed class LatestWinsWatermarkHelper : IWMWatermarkHelper
    {
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }

        public async Task<WMDesignRenderResult> GenerationDesignPreviewAsync(
            WMCanvas mainCanvas,
            WMZipedTemplate? ziped,
            CancellationToken cancellationToken = default)
        {
            var call = ++CallCount;
            if (call == 1)
            {
                FirstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new WMDesignRenderResult(
                [1, 2, 3],
                100,
                80,
                new WMDesignViewport(0, 0, 100, 80),
                []);
        }

        public Task<byte[]> GenerationAsync(WMCanvas mainCanvas, WMZipedTemplate? ziped, bool isPreview, bool designMode = false) => throw new NotSupportedException();
        public byte[] Generation(WMCanvas mainCanvas, WMZipedTemplate? ziped, bool isPreview, bool designMode = false) => throw new NotSupportedException();
        public Task<byte[]> SplitImages(List<string> images, bool horizon, bool preview) => throw new NotSupportedException();
    }

    private sealed class FakeObjectUrlRegistry : IWMObjectUrlRegistry
    {
        public List<long> PublishedVersions { get; } = [];
        public List<string> PublishedMimeTypes { get; } = [];
        public List<long> ReleasedVersions { get; } = [];
        public int ActiveLeaseCount => PublishedVersions.Count - ReleasedVersions.Count;

        public ValueTask<WMObjectUrlLease?> PublishAsync(
            string ownerKey,
            long ownerVersion,
            Stream content,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishedVersions.Add(ownerVersion);
            PublishedMimeTypes.Add(mimeType);
            return ValueTask.FromResult<WMObjectUrlLease?>(new WMObjectUrlLease(
                ownerKey,
                ownerVersion,
                $"blob:{ownerVersion}",
                ownerVersion));
        }

        public ValueTask ReleaseAsync(WMObjectUrlLease lease)
        {
            ReleasedVersions.Add(lease.OwnerVersion);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
