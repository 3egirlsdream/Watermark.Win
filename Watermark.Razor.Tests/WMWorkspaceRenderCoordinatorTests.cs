using Watermark.Razor.Workspace;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWorkspaceRenderCoordinatorTests
{
    [Fact]
    public async Task RapidChanges_OnlyPublishLatestVersion()
    {
        using var coordinator = new WMWorkspaceRenderCoordinator();
        var published = new List<long>();
        coordinator.PreviewPublished += preview => published.Add(preview.Version);
        var tasks = new List<Task<WMWorkspacePreview>>();
        var renderCalls = 0;

        for (var version = 1; version <= 50; version++)
        {
            var captured = version;
            tasks.Add(coordinator.QueuePreview(new WMWorkspaceRenderRequest(
                "session",
                1,
                captured,
                $"fingerprint-{captured}",
                async token =>
                {
                    Interlocked.Increment(ref renderCalls);
                    await Task.Delay(captured == 50 ? 5 : 100, token);
                    return Preview(captured, $"fingerprint-{captured}");
                })).Completion);
        }

        foreach (var task in tasks)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }

        Assert.Equal(50, renderCalls);
        Assert.Equal([50L], published);
    }

    [Fact]
    public async Task Flush_WaitsForCurrentRenderWithoutStartingAnotherOne()
    {
        using var coordinator = new WMWorkspaceRenderCoordinator();
        var calls = 0;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticket = coordinator.QueuePreview(new WMWorkspaceRenderRequest(
            "session", 1, 1, "same",
            async token =>
            {
                Interlocked.Increment(ref calls);
                await completion.Task.WaitAsync(token);
                return Preview(1, "same");
            }));
        var queued = ticket.Completion;

        var flushed = coordinator.FlushAsync(ticket);
        Assert.Equal(1, calls);
        completion.SetResult();

        Assert.Equal(1, (await queued).Version);
        Assert.Equal(1, (await flushed).Version);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CompletedFingerprint_IsReusedWithoutRenderingAgain()
    {
        var path = Path.Combine(Path.GetTempPath(), $"watermark-render-cache-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, [1]);
        try
        {
        using var coordinator = new WMWorkspaceRenderCoordinator();
        var calls = 0;
        await coordinator.QueuePreview(new WMWorkspaceRenderRequest(
            "session", 1, 1, "cached",
            _ =>
            {
                calls++;
                return Task.FromResult(Preview(1, "cached", path));
            })).Completion;

        var second = await coordinator.QueuePreview(new WMWorkspaceRenderRequest(
            "session", 1, 2, "cached",
            _ =>
            {
                calls++;
                return Task.FromResult(Preview(2, "cached", path));
            })).Completion;

        Assert.Equal(2, second.Version);
        Assert.Equal(1, calls);
        Assert.True(second.CacheHit);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task MissingCachedFile_IsInvalidatedAndRenderedAgain()
    {
        var path = Path.Combine(Path.GetTempPath(), $"watermark-render-missing-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, [1]);
        try
        {
            using var coordinator = new WMWorkspaceRenderCoordinator();
            var calls = 0;
            await coordinator.QueuePreview(new WMWorkspaceRenderRequest(
                "session", 1, 1, "cached",
                _ =>
                {
                    calls++;
                    return Task.FromResult(Preview(1, "cached", path));
                })).Completion;
            File.Delete(path);

            var second = await coordinator.QueuePreview(new WMWorkspaceRenderRequest(
                "session", 1, 2, "cached",
                _ =>
                {
                    calls++;
                    File.WriteAllBytes(path, [2]);
                    return Task.FromResult(Preview(2, "cached", path));
                })).Completion;

            Assert.Equal(2, second.Version);
            Assert.Equal(2, calls);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task OlderRequestArrivingLate_IsCanceledWithoutReplacingLatest()
    {
        using var coordinator = new WMWorkspaceRenderCoordinator();
        var published = new List<long>();
        var olderRenderCalls = 0;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.PreviewPublished += preview => published.Add(preview.Version);

        var latest = coordinator.QueuePreview(new WMWorkspaceRenderRequest(
            "session", 1, 2, "latest",
            async token =>
            {
                await completion.Task.WaitAsync(token);
                return Preview(2, "latest");
            })).Completion;

        var stale = coordinator.QueuePreview(new WMWorkspaceRenderRequest(
            "session", 1, 1, "stale",
            _ =>
            {
                Interlocked.Increment(ref olderRenderCalls);
                return Task.FromResult(Preview(1, "stale"));
            })).Completion;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stale);
        completion.SetResult();

        Assert.Equal(2, (await latest).Version);
        Assert.Equal(0, olderRenderCalls);
        Assert.Equal([2L], published);
    }

    [Fact]
    public async Task SameFingerprint_AfterCanceledRender_StartsFreshRender()
    {
        using var coordinator = new WMWorkspaceRenderCoordinator();
        using var firstCancellation = new CancellationTokenSource();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<long>();
        var renderCalls = 0;
        coordinator.PreviewPublished += preview => published.Add(preview.Version);

        var first = coordinator.QueuePreview(new WMWorkspaceRenderRequest(
            "session", 1, 1, "same",
            async token =>
            {
                Interlocked.Increment(ref renderCalls);
                firstStarted.SetResult();
                await releaseFirstRender.Task;
                token.ThrowIfCancellationRequested();
                return Preview(1, "same");
            }), firstCancellation.Token).Completion;

        await firstStarted.Task;
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        var second = coordinator.QueuePreview(new WMWorkspaceRenderRequest(
            "session", 1, 2, "same",
            _ =>
            {
                Interlocked.Increment(ref renderCalls);
                return Task.FromResult(Preview(2, "same"));
            })).Completion;

        Assert.Equal(2, (await second).Version);
        releaseFirstRender.SetResult();
        Assert.Equal(2, renderCalls);
        Assert.Equal([2L], published);
    }

    private static WMWorkspacePreview Preview(long version, string fingerprint, string? path = null) =>
        new(version, fingerprint, path ?? $"/tmp/{version}.png", "image/png", 100, 100);
}
