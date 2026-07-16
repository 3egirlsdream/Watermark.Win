using Microsoft.JSInterop;
using Watermark.Razor.Workspace;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMObjectUrlRegistryTests
{
    [Fact]
    public async Task ReplacingOwner_ReleasesOnlyPreviousLease()
    {
        var runtime = new RecordingJsRuntime();
        var metrics = new WMWorkspacePerformanceCounters();
        await using var registry = new WMObjectUrlRegistry(runtime, metrics);

        var first = await PublishAsync(registry, "preview", 1, 1);
        var history = await PublishAsync(registry, "history", 1, 2);
        var second = await PublishAsync(registry, "preview", 2, 3);

        Assert.NotEqual(first!.Url, second!.Url);
        Assert.Equal(2, registry.ActiveLeaseCount);
        Assert.Contains(first.Url, runtime.Revoked);
        Assert.DoesNotContain(history!.Url, runtime.Revoked);
        Assert.Equal(3, metrics.Snapshot().Calls[WMWorkspaceMetricStage.BlobCreate]);
    }

    [Fact]
    public async Task Dispose_ReleasesEveryRemainingOwner()
    {
        var runtime = new RecordingJsRuntime();
        var registry = new WMObjectUrlRegistry(runtime, new WMWorkspacePerformanceCounters());
        var first = await PublishAsync(registry, "a", 1, 1);
        var second = await PublishAsync(registry, "b", 1, 2);

        await registry.DisposeAsync();

        Assert.Contains(first!.Url, runtime.Revoked);
        Assert.Contains(second!.Url, runtime.Revoked);
        Assert.Equal(0, registry.ActiveLeaseCount);
    }

    [Fact]
    public async Task ReleasingOldGeneration_DoesNotReleaseNewUrl()
    {
        var runtime = new RecordingJsRuntime();
        await using var registry = new WMObjectUrlRegistry(runtime, new WMWorkspacePerformanceCounters());
        var first = await PublishAsync(registry, "preview", 1, 1);
        var second = await PublishAsync(registry, "preview", 2, 2);

        await registry.ReleaseAsync(first!);

        Assert.Equal(1, registry.ActiveLeaseCount);
        Assert.DoesNotContain(second!.Url, runtime.Revoked);
    }

    [Fact]
    public async Task OlderOwnerVersion_CannotReplaceNewUrl()
    {
        var runtime = new RecordingJsRuntime();
        await using var registry = new WMObjectUrlRegistry(runtime, new WMWorkspacePerformanceCounters());
        var current = await PublishAsync(registry, "preview", 2, 2);

        var stale = await PublishAsync(registry, "preview", 1, 1);

        Assert.Null(stale);
        Assert.Equal(1, registry.ActiveLeaseCount);
        Assert.DoesNotContain(current!.Url, runtime.Revoked);
    }

    [Fact]
    public async Task LatePublish_RevokesOnlyItsCandidate()
    {
        var runtime = new DelayedFirstJsRuntime();
        await using var registry = new WMObjectUrlRegistry(runtime, new WMWorkspacePerformanceCounters());
        var firstTask = PublishAsync(registry, "preview", 1, 1);
        await runtime.FirstStarted.Task;

        var latest = await PublishAsync(registry, "preview", 2, 2);
        runtime.ReleaseFirst.SetResult();
        var stale = await firstTask;

        Assert.Null(stale);
        Assert.Equal(1, registry.ActiveLeaseCount);
        Assert.Contains("blob:1", runtime.Revoked);
        Assert.DoesNotContain(latest!.Url, runtime.Revoked);
    }

    private static async Task<WMObjectUrlLease?> PublishAsync(
        IWMObjectUrlRegistry registry,
        string owner,
        long version,
        byte value)
    {
        await using var content = new MemoryStream([value], writable: false);
        return await registry.PublishAsync(owner, version, content, "image/png");
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        private int next;
        public List<string> Revoked { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier == "watermarkObjectUrls.createFromStream")
                return ValueTask.FromResult((TValue)(object)$"blob:{Interlocked.Increment(ref next)}");
            if (identifier == "watermarkObjectUrls.revoke")
            {
                Revoked.Add((string)args![0]!);
                return ValueTask.FromResult(default(TValue)!);
            }
            throw new InvalidOperationException(identifier);
        }
    }

    private sealed class DelayedFirstJsRuntime : IJSRuntime
    {
        private int next;
        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Revoked { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier == "watermarkObjectUrls.createFromStream")
            {
                var call = Interlocked.Increment(ref next);
                if (call == 1)
                {
                    FirstStarted.SetResult();
                    await ReleaseFirst.Task.WaitAsync(cancellationToken);
                }
                return (TValue)(object)$"blob:{call}";
            }
            if (identifier == "watermarkObjectUrls.revoke")
            {
                Revoked.Add((string)args![0]!);
                return default!;
            }
            throw new InvalidOperationException(identifier);
        }
    }
}
