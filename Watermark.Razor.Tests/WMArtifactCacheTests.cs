using Watermark.Razor.Workspace;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMArtifactCacheTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "watermark-artifact-cache-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CommitAndLookup_PersistIndex_AndMissingFileInvalidatesEntry()
    {
        var cache = new WMArtifactCache();
        var path = CreateArtifact("first.bin", 32);

        var committed = await cache.CommitAsync(root, "fingerprint-a", path, 1024);
        var cached = await cache.TryGetAsync(root, "fingerprint-a");

        Assert.Equal(Path.GetFullPath(path), committed.FilePath);
        Assert.Equal(32, committed.Length);
        Assert.Equal(committed.FilePath, cached!.FilePath);
        Assert.True(File.Exists(Path.Combine(root, "artifacts", "cache-index.json")));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories));

        File.Delete(path);

        Assert.Null(await cache.TryGetAsync(root, "fingerprint-a"));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Trim_UsesLruButNeverDeletesActivelyLeasedArtifact()
    {
        var cache = new WMArtifactCache();
        var first = CreateArtifact("first.bin", 40);
        var second = CreateArtifact("second.bin", 40);
        await cache.CommitAsync(root, "first", first, 1024);
        await cache.CommitAsync(root, "second", second, 1024);

        using (cache.AcquireLease(root, "first"))
        {
            await cache.TrimAsync(root, 40);

            Assert.True(File.Exists(first));
            Assert.False(File.Exists(second));
            Assert.NotNull(await cache.TryGetAsync(root, "first"));
            Assert.Null(await cache.TryGetAsync(root, "second"));
        }

        await cache.TrimAsync(root, 1);

        Assert.False(File.Exists(first));
        Assert.Null(await cache.TryGetAsync(root, "first"));
    }

    [Fact]
    public async Task Commit_RejectsFilesOutsideSessionDirectory()
    {
        Directory.CreateDirectory(root);
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(outside, [1, 2, 3]);
        try
        {
            var cache = new WMArtifactCache();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                cache.CommitAsync(root, "outside", outside, 1024));
        }
        finally
        {
            try { File.Delete(outside); } catch { }
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }

    private string CreateArtifact(string fileName, int length)
    {
        var directory = Path.Combine(root, "operations", "preview");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, Enumerable.Range(0, length).Select(value => (byte)value).ToArray());
        return path;
    }
}
