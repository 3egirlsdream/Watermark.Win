using Microsoft.JSInterop;
using Watermark.Razor.Workspace;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMSceneSurfaceTransportTests
{
    [Fact]
    public async Task LateOlderPublish_CannotReplaceNewerBitmapLease()
    {
        var js = new FakeJsRuntime();
        await using var transport = new WMSceneSurfaceTransport(js);
        await using var firstStream = new MemoryStream([1, 2, 3], writable: false);
        await using var secondStream = new MemoryStream([4, 5, 6], writable: false);

        var firstTask = transport.PublishAsync(
            "designer:layer:text",
            1,
            firstStream,
            "image/png").AsTask();
        await js.Module.FirstPublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var latest = await transport.PublishAsync(
            "designer:layer:text",
            2,
            secondStream,
            "image/png");
        var duplicate = await transport.PublishAsync(
            "designer:layer:text",
            2,
            secondStream,
            "image/png");
        js.Module.ReleaseFirstPublish.TrySetResult();
        var stale = await firstTask;

        Assert.NotNull(latest);
        Assert.Equal(latest, duplicate);
        Assert.Null(stale);
        Assert.Equal(2, js.Module.PublishCount);
        Assert.Equal(
            latest.ResourceKey,
            Assert.Single(js.Module.ActiveKeys));
        Assert.Single(js.Module.ReleasedKeys);

        await transport.DisposeAsync();
        await transport.DisposeAsync();
        Assert.Empty(js.Module.ActiveKeys);
        Assert.True(js.Module.Disposed);
    }

    [Fact]
    public async Task Dispose_WaitsForInflightDecodeAndReleasesItsCandidate()
    {
        var js = new FakeJsRuntime();
        var transport = new WMSceneSurfaceTransport(js);
        await using var stream = new MemoryStream([1, 2, 3], writable: false);
        var publish = transport.PublishAsync(
            "designer:base",
            1,
            stream,
            "image/png").AsTask();
        await js.Module.FirstPublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var dispose = transport.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        js.Module.ReleaseFirstPublish.TrySetResult();

        Assert.Null(await publish);
        await dispose;
        Assert.Empty(js.Module.ActiveKeys);
        Assert.Single(js.Module.ReleasedKeys);
        Assert.True(js.Module.Disposed);
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        public BlockingSceneModule Module { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(identifier, "import", StringComparison.Ordinal))
                throw new InvalidOperationException($"不支持的 JS 调用：{identifier}");
            return ValueTask.FromResult((TValue)(object)Module);
        }
    }

    private sealed class BlockingSceneModule : IJSObjectReference
    {
        private readonly object gate = new();
        private readonly HashSet<string> activeKeys = new(StringComparer.Ordinal);
        private readonly List<string> releasedKeys = [];
        private int publishCount;

        public TaskCompletionSource FirstPublishStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstPublish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PublishCount => Volatile.Read(ref publishCount);
        public bool Disposed { get; private set; }
        public IReadOnlyList<string> ActiveKeys
        {
            get
            {
                lock (gate) return activeKeys.ToArray();
            }
        }
        public IReadOnlyList<string> ReleasedKeys
        {
            get
            {
                lock (gate) return releasedKeys.ToArray();
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            InvokeCoreAsync<TValue>(identifier, cancellationToken, args);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private async ValueTask<TValue> InvokeCoreAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (string.Equals(identifier, "publishSceneBitmap", StringComparison.Ordinal))
            {
                var key = Assert.IsType<string>(args![0]);
                var call = Interlocked.Increment(ref publishCount);
                if (call == 1)
                {
                    FirstPublishStarted.TrySetResult();
                    await ReleaseFirstPublish.Task.WaitAsync(cancellationToken);
                }
                lock (gate) activeKeys.Add(key);
                return (TValue)(object)true;
            }

            if (string.Equals(identifier, "releaseSceneBitmap", StringComparison.Ordinal))
            {
                var key = Assert.IsType<string>(args![0]);
                lock (gate)
                {
                    activeKeys.Remove(key);
                    releasedKeys.Add(key);
                }
                return default!;
            }

            throw new InvalidOperationException($"不支持的模块调用：{identifier}");
        }
    }
}
