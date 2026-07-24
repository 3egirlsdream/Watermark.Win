using Microsoft.Extensions.DependencyInjection;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMTemplateDesignerSessionTests
{
    [Fact]
    public async Task ApplicationServices_ResolveSceneSessionWithoutConstructorAmbiguity()
    {
        var services = new ServiceCollection();
        services.AddWMApplicationServices();
        services.AddSingleton<IWMDesignSceneRenderer>(new StaticSceneRenderer());
        services.AddSingleton<IWMObjectUrlRegistry>(new FakeObjectUrlRegistry());
        services.AddSingleton<IWMSceneSurfaceTransport>(new FakeSceneSurfaceTransport());
        services.AddSingleton<IWMWorkspacePerformanceCounters, WMWorkspacePerformanceCounters>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<WMTemplateDesignerSession>();

        Assert.NotNull(session);
    }

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

    [Fact]
    public async Task RenderPreviewAsync_OneHundredRapidUpdatesPublishOnlyLatest()
    {
        var helper = new NonInterruptibleWatermarkHelper();
        var urls = new FakeObjectUrlRegistry();
        await using var session = new WMTemplateDesignerSession(helper, urls);

        var first = session.RenderPreviewAsync(
            "designer",
            new WMCanvas { Name = "first" });
        await helper.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var updates = Enumerable.Range(1, 100)
            .Select(index => session.RenderPreviewAsync(
                "designer",
                new WMCanvas { Name = $"update-{index}" }))
            .ToArray();

        helper.ReleaseFirst.TrySetResult();
        var firstResult = await first;
        var results = await Task.WhenAll(updates);

        Assert.Null(firstResult);
        Assert.Equal(99, results.Count(result => result is null));
        Assert.NotNull(results[^1]);
        Assert.Equal("update-100", helper.RenderedNames[^1]);
        Assert.Equal(2, helper.CallCount);
        Assert.Equal(1, helper.MaximumConcurrency);
        Assert.Equal([101L], urls.PublishedVersions);
    }

    [Fact]
    public async Task ClearAsync_ReleasesEverySceneLeaseAndDisposesNativeSession()
    {
        var renderer = new StaticSceneRenderer();
        var urls = new FakeObjectUrlRegistry();
        await using var session = new WMTemplateDesignerSession(renderer, urls);

        var preview = await session.RenderSceneAsync(
            "designer",
            new WMCanvas { Name = "scene" },
            WMTemplateChangeSet.Initial(1),
            WMDesignSceneQuality.Exact);

        Assert.NotNull(preview?.Scene);
        Assert.Equal(2, preview.Scene.Layers.Count);
        Assert.Equal(3, urls.ActiveLeaseCount);

        await session.ClearAsync();

        Assert.True(renderer.Session.Disposed);
        Assert.Equal(0, urls.ActiveLeaseCount);
        Assert.Equal(3, urls.ReleasedVersions.Count);
    }

    [Fact]
    public async Task ScenePipeline_RecordsLayoutRasterCompositeAndUploadStages()
    {
        var renderer = new StaticSceneRenderer();
        var urls = new FakeObjectUrlRegistry();
        var metrics = new WMWorkspacePerformanceCounters();
        await using var session = new WMTemplateDesignerSession(
            renderer,
            urls,
            new FakeSceneSurfaceTransport(),
            metrics);

        await session.RenderSceneAsync(
            "designer",
            new WMCanvas { Name = "scene" },
            WMTemplateChangeSet.Initial(1),
            WMDesignSceneQuality.Exact);

        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.SceneLayout]);
        Assert.Equal(2, snapshot.Calls[WMWorkspaceMetricStage.LayerRaster]);
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.SceneComposite]);
        Assert.Equal(3, snapshot.Calls[WMWorkspaceMetricStage.LayerUpload]);
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.LayerCacheMiss]);
        Assert.True(snapshot.DurationMilliseconds[WMWorkspaceMetricStage.LayerUpload] >= 0);
    }

    [Fact]
    public async Task SceneInitializationFailure_ReleasesPartialSceneAndUsesSharedFallback()
    {
        var renderer = new FailingSceneRenderer();
        var urls = new FakeObjectUrlRegistry();
        await using var session = new WMTemplateDesignerSession(renderer, urls);

        var preview = await session.RenderSceneAsync(
            "designer",
            new WMCanvas { Name = "fallback" },
            WMTemplateChangeSet.Initial(1),
            WMDesignSceneQuality.Exact);

        Assert.NotNull(preview);
        Assert.True(preview.UsesCompatibilityFallback);
        Assert.Null(preview.Scene);
        Assert.Equal("blob:1", preview.PreviewUrl);
        Assert.Equal(1, renderer.FallbackCount);
        Assert.Equal(1, urls.ActiveLeaseCount);

        await session.ClearAsync();
        Assert.Equal(0, urls.ActiveLeaseCount);
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

    private sealed class StaticSceneRenderer : IWMDesignSceneRenderer
    {
        public StaticSceneSession Session { get; } = new();

        public ValueTask<IWMDesignSceneSession> OpenSessionAsync(
            WMCanvas canvas,
            WMZipedTemplate? ziped = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IWMDesignSceneSession>(Session);
        }
    }

    private sealed class FailingSceneRenderer : IWMDesignSceneFallbackRenderer
    {
        public int FallbackCount { get; private set; }

        public ValueTask<IWMDesignSceneSession> OpenSessionAsync(
            WMCanvas canvas,
            WMZipedTemplate? ziped = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("scene failed");

        public Task<WMDesignRenderResult> RenderCompatibilityAsync(
            WMCanvas canvas,
            WMZipedTemplate? ziped = null,
            CancellationToken cancellationToken = default)
        {
            FallbackCount++;
            return Task.FromResult(new WMDesignRenderResult(
                [0xff, 0xd8, 0xff, 0x01],
                100,
                80,
                new WMDesignViewport(0, 0, 100, 80),
                []));
        }
    }

    private sealed class StaticSceneSession : IWMDesignSceneSession
    {
        public bool Disposed { get; private set; }
        public WMDesignSceneFrame CurrentFrame { get; } = CreateFrame();
        public WMDesignSceneMetricsSnapshot Metrics { get; } =
            new(1, 1, 2, 1, 0, 0, 3);

        public Task<WMDesignSceneUpdate> UpdateAsync(
            WMCanvas canvas,
            WMTemplateChangeSet changeSet,
            WMDesignSceneQuality quality,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WMDesignSceneUpdate(
                CurrentFrame,
                WMDesignScenePatch.Empty(CurrentFrame.Revision),
                true,
                0));

        public Task<WMDesignRenderResult> FlushAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentFrame.ToRenderResult([0xff, 0xd8, 0xff, 0x01]));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private static WMDesignSceneFrame CreateFrame()
        {
            var viewport = new WMDesignViewport(0, 0, 100, 80);
            return new WMDesignSceneFrame(
                1,
                100,
                80,
                viewport,
                null,
                "base",
                1,
                [0xff, 0xd8, 0xff, 0x01],
                [
                    CreateLayer("TEXT", 0),
                    CreateLayer("LOGO", 1)
                ],
                true);
        }

        private static WMDesignSceneLayer CreateLayer(string id, int zIndex) =>
            new(
                id,
                null,
                id,
                "WMText",
                zIndex,
                new WMDesignBounds(
                    id,
                    null,
                    "WMText",
                    0,
                    0,
                    20,
                    10,
                    100,
                    80,
                    new WMTransform(),
                    false,
                    true),
                new WMDesignSceneRect(0, 0, 20, 10),
                null,
                WMOverflow.Visible,
                true,
                false,
                WMDesignSceneCompositeMode.Normal,
                0,
                0,
                $"surface:{id}",
                1,
                [0xff, 0xd8, 0xff, (byte)(zIndex + 2)]);
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

    private sealed class FakeSceneSurfaceTransport : IWMSceneSurfaceTransport
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
}
