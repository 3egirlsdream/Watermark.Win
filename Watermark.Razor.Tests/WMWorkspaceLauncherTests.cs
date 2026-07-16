using Watermark.Razor.Workspace;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWorkspaceLauncherTests
{
    [Fact]
    public async Task Create_VerifiesPersistedSessionBeforeReturning()
    {
        var store = new FakeSessionStore(WMWorkspaceOpenResult.Opened(new WMWorkspaceSession
        {
            Id = FakeSessionStore.SessionId
        }));
        var disposed = false;
        var source = Source(() => disposed = true);

        var result = await new WMWorkspaceLauncher(store).CreateFromSourcesAsync(
            WMWorkspaceMode.Template,
            [source],
            null,
            CancellationToken.None);

        Assert.Equal(FakeSessionStore.SessionId, result);
        Assert.Equal(FakeSessionStore.SessionId, store.OpenedSessionId);
        Assert.Null(store.DeletedSessionId);
        Assert.True(disposed);
    }

    [Fact]
    public async Task Create_WhenManifestCannotBeReopened_DeletesOrphanAndFailsBeforeNavigation()
    {
        var store = new FakeSessionStore(new WMWorkspaceOpenResult(
            WMWorkspaceOpenStatus.Missing,
            null,
            []));
        var disposed = false;
        var source = Source(() => disposed = true);

        var error = await Assert.ThrowsAsync<IOException>(() =>
            new WMWorkspaceLauncher(store).CreateFromSourcesAsync(
                WMWorkspaceMode.Template,
                [source],
                null,
                CancellationToken.None));

        Assert.Contains("未能持久化", error.Message, StringComparison.Ordinal);
        Assert.Equal(FakeSessionStore.SessionId, store.OpenedSessionId);
        Assert.Equal(FakeSessionStore.SessionId, store.DeletedSessionId);
        Assert.True(disposed);
    }

    private static WMPhotoImportSource Source(Action disposed) =>
        new(
            "picked.jpg",
            _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3], writable: false)),
            disposeAsync: () =>
            {
                disposed();
                return ValueTask.CompletedTask;
            });

    private sealed class FakeSessionStore(WMWorkspaceOpenResult openResult) : IWMWorkspaceSessionStore
    {
        public const string SessionId = "new-session";

        public string? OpenedSessionId { get; private set; }
        public string? DeletedSessionId { get; private set; }

        public IDisposable AcquireLease(string sessionId) => new NoopDisposable();
        public string GetSessionDirectory(string sessionId) => sessionId;

        public Task<string> CreateAsync(WMWorkspaceCreateRequest request, CancellationToken token = default) =>
            Task.FromResult(SessionId);

        public Task<string> CreateAsync(
            WMWorkspaceMode mode,
            IReadOnlyList<IWMPhotoImportSource> sources,
            string? templateId = null,
            CancellationToken token = default) => Task.FromResult(SessionId);

        public Task<WMWorkspaceOpenResult> OpenAsync(
            string sessionId,
            CancellationToken token = default)
        {
            OpenedSessionId = sessionId;
            return Task.FromResult(openResult);
        }

        public Task<WMWorkspaceOpenResult> RecoverAsync(
            string sessionId,
            WMWorkspaceRecoveryAction action,
            IReadOnlyList<string> affectedIds,
            CancellationToken token = default) => Task.FromResult(openResult);

        public Task SaveAsync(WMWorkspaceSession session, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string sessionId)
        {
            DeletedSessionId = sessionId;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WMWorkspaceSession>> ListRecentAsync(
            int take = 5,
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<WMWorkspaceSession>>([]);

        public Task CleanupExpiredAsync(CancellationToken token = default) => Task.CompletedTask;

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
