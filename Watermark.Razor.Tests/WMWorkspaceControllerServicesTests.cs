using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWorkspaceControllerServicesTests
{
    [Fact]
    public async Task DurableQueue_SerializesConcurrentCommands()
    {
        var queue = new WMDurableCommandQueue();
        var active = 0;
        var peak = 0;
        var completed = 0;
        var tasks = Enumerable.Range(0, 20).Select(_ => queue.RunAsync(async () =>
        {
            var current = Interlocked.Increment(ref active);
            peak = Math.Max(peak, current);
            await Task.Delay(2);
            Interlocked.Decrement(ref active);
            Interlocked.Increment(ref completed);
        }));

        await Task.WhenAll(tasks);

        Assert.Equal(1, peak);
        Assert.Equal(20, completed);
    }

    [Fact]
    public void JobCoordinator_NewJobCancelsPreviousWithoutCancelingLatest()
    {
        var jobs = new WMWorkspaceJobCoordinator();
        using var first = jobs.Begin(CancellationToken.None);
        using var second = jobs.Begin(CancellationToken.None);

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);

        jobs.Complete(first);
        Assert.False(second.IsCancellationRequested);
        jobs.Cancel();
        Assert.True(second.IsCancellationRequested);
    }

    [Fact]
    public void Projection_UsesCurrentArtifactAndDurableHistory()
    {
        var source = Artifact("source", "source.jpg");
        var edited = Artifact("edited", "edited.jpg");
        var media = new WMWorkspaceMedia
        {
            Id = "media",
            DisplayName = "photo.jpg",
            OriginalReference = source.FilePath,
            Artifact = source,
            IsSelected = false
        };
        var transaction = new WMWorkspaceTransaction
        {
            Id = "tx",
            Label = "调色",
            CreatedAtUtc = DateTime.UtcNow
        };
        var session = new WMWorkspaceSession
        {
            Id = "session",
            Media = [media],
            MediaCatalog = [media],
            ActiveMediaIds = [media.Id],
            Artifacts = [source, edited],
            CurrentArtifactIdsByMediaId = new Dictionary<string, string> { [media.Id] = edited.Id },
            SelectedMediaIds = [media.Id],
            Transactions = [transaction],
            HistoryCursor = 1
        };

        var projected = Assert.Single(WMWorkspaceProjection.Media(session));
        var history = Assert.Single(WMWorkspaceProjection.History(session));

        Assert.Equal(edited.Id, projected.Artifact.Id);
        Assert.True(projected.IsSelected);
        Assert.True(history.IsApplied);
        Assert.Equal("调色", history.Label);
    }

    private static WMImageArtifact Artifact(string id, string path) => new()
    {
        Id = id,
        FilePath = path,
        ContentHash = id,
        Width = 10,
        Height = 10,
        CreatedAtUtc = DateTime.UtcNow
    };
}
