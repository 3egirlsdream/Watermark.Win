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
    public async Task RenderPreviewAsync_MobilePublishesEncodedBytesInlineWithoutCreatingBlob()
    {
        var helper = new InlineWatermarkHelper();
        var urls = new FakeObjectUrlRegistry();
        await using var session = new WMTemplateDesignerSession(helper, urls);

        var first = await session.RenderPreviewAsync(
            "mobile-designer",
            new WMCanvas { Name = "first" },
            publishObjectUrl: false);
        var second = await session.RenderPreviewAsync(
            "mobile-designer",
            new WMCanvas { Name = "second" },
            publishObjectUrl: false);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.StartsWith("data:image/jpeg;base64,", first.PreviewUrl, StringComparison.Ordinal);
        Assert.StartsWith("data:image/jpeg;base64,", second.PreviewUrl, StringComparison.Ordinal);
        Assert.NotEqual(first.PreviewUrl, second.PreviewUrl);
        Assert.Equal(2, helper.CallCount);
        Assert.Empty(urls.PublishedVersions);
        Assert.Equal(0, urls.ActiveLeaseCount);
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

    [Fact]
    public async Task RenderPreviewAsync_CoalescesRapidChangesWithoutParallelRendering()
    {
        var helper = new NonInterruptibleWatermarkHelper();
        var urls = new FakeObjectUrlRegistry();
        await using var session = new WMTemplateDesignerSession(helper, urls);

        var first = session.RenderPreviewAsync("designer", new WMCanvas { Name = "first" });
        await helper.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var middle = session.RenderPreviewAsync("designer", new WMCanvas { Name = "middle" });
        var latest = session.RenderPreviewAsync("designer", new WMCanvas { Name = "latest" });

        await Task.Yield();
        Assert.Equal(1, helper.CallCount);
        Assert.Equal(1, helper.MaximumConcurrency);

        helper.ReleaseFirst.TrySetResult();
        Assert.Null(await first);
        Assert.Null(await middle);
        var rendered = await latest;

        Assert.NotNull(rendered);
        Assert.Equal(["first", "latest"], helper.RenderedNames);
        Assert.Equal(2, helper.CallCount);
        Assert.Equal(1, helper.MaximumConcurrency);
        Assert.Equal([3L], urls.PublishedVersions);
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

    private sealed class InlineWatermarkHelper : IWMWatermarkHelper
    {
        public int CallCount { get; private set; }

        public Task<WMDesignRenderResult> GenerationDesignPreviewAsync(
            WMCanvas mainCanvas,
            WMZipedTemplate? ziped,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var marker = mainCanvas.Name == "second" ? (byte)0x02 : (byte)0x01;
            return Task.FromResult(new WMDesignRenderResult(
                [0xff, 0xd8, 0xff, marker],
                100,
                80,
                new WMDesignViewport(0, 0, 100, 80),
                []));
        }

        public Task<byte[]> GenerationAsync(WMCanvas mainCanvas, WMZipedTemplate? ziped, bool isPreview, bool designMode = false) => throw new NotSupportedException();
        public byte[] Generation(WMCanvas mainCanvas, WMZipedTemplate? ziped, bool isPreview, bool designMode = false) => throw new NotSupportedException();
        public Task<byte[]> SplitImages(List<string> images, bool horizon, bool preview) => throw new NotSupportedException();
    }

    private sealed class NonInterruptibleWatermarkHelper : IWMWatermarkHelper
    {
        private int activeRenders;

        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> RenderedNames { get; } = [];
        public int CallCount { get; private set; }
        public int MaximumConcurrency { get; private set; }

        public async Task<WMDesignRenderResult> GenerationDesignPreviewAsync(
            WMCanvas mainCanvas,
            WMZipedTemplate? ziped,
            CancellationToken cancellationToken = default)
        {
            var call = ++CallCount;
            RenderedNames.Add(mainCanvas.Name);
            var concurrency = Interlocked.Increment(ref activeRenders);
            MaximumConcurrency = Math.Max(MaximumConcurrency, concurrency);
            try
            {
                if (call == 1)
                {
                    FirstStarted.TrySetResult();
                    // Deliberately model the current Skia render section: once
                    // entered, cancellation is observed only after it returns.
                    await ReleaseFirst.Task;
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new WMDesignRenderResult(
                    [1, 2, 3],
                    100,
                    80,
                    new WMDesignViewport(0, 0, 100, 80),
                    []);
            }
            finally
            {
                Interlocked.Decrement(ref activeRenders);
            }
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
