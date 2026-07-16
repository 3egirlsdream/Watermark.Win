#nullable enable

using System.Collections.Concurrent;
using System.Text.Json;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Session-local persistent artifact index with atomic writes and LRU cleanup.
/// Only indexed files are ever deleted.
/// </summary>
public sealed class WMArtifactCache : IWMArtifactCache
{
    private const int SchemaVersion = 1;
    private const string IndexFileName = "cache-index.json";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> IndexLocks =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> ActiveLeases =
        new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<WMArtifactCacheEntry?> TryGetAsync(
        string sessionDirectory,
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        var context = Context(sessionDirectory, fingerprint);
        var indexLock = IndexLocks.GetOrAdd(context.IndexPath, _ => new SemaphoreSlim(1, 1));
        await indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadAsync(context.IndexPath, cancellationToken).ConfigureAwait(false);
            if (!index.Entries.TryGetValue(fingerprint, out var stored)) return null;
            var path = ResolveIndexedPath(context.SessionRoot, stored.RelativePath);
            if (!IsValid(path, stored.Length))
            {
                index.Entries.Remove(fingerprint);
                await SaveAsync(context.IndexPath, index, cancellationToken).ConfigureAwait(false);
                return null;
            }

            var touched = stored with { LastAccessUtc = DateTime.UtcNow };
            index.Entries[fingerprint] = touched;
            await SaveAsync(context.IndexPath, index, cancellationToken).ConfigureAwait(false);
            return ToPublic(fingerprint, path, touched);
        }
        finally
        {
            indexLock.Release();
        }
    }

    public async Task<WMArtifactCacheEntry> CommitAsync(
        string sessionDirectory,
        string fingerprint,
        string filePath,
        long budgetBytes,
        CancellationToken cancellationToken = default)
    {
        var context = Context(sessionDirectory, fingerprint);
        var fullPath = Path.GetFullPath(filePath);
        EnsureInsideSession(context.SessionRoot, fullPath);
        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length <= 0)
            throw new InvalidDataException("不能缓存不存在或为空的产物。");

        var indexLock = IndexLocks.GetOrAdd(context.IndexPath, _ => new SemaphoreSlim(1, 1));
        await indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var info = new FileInfo(fullPath);
            var index = await LoadAsync(context.IndexPath, cancellationToken).ConfigureAwait(false);
            var stored = new StoredEntry(
                Path.GetRelativePath(context.SessionRoot, fullPath),
                info.Length,
                DateTime.UtcNow);
            index.Entries[fingerprint] = stored;
            TrimLocked(context, index, Math.Max(1, budgetBytes), fingerprint);
            await SaveAsync(context.IndexPath, index, cancellationToken).ConfigureAwait(false);
            return ToPublic(fingerprint, fullPath, stored);
        }
        finally
        {
            indexLock.Release();
        }
    }

    public IDisposable AcquireLease(string sessionDirectory, string fingerprint)
    {
        var context = Context(sessionDirectory, fingerprint);
        ActiveLeases.AddOrUpdate(context.LeaseKey, 1, (_, count) => checked(count + 1));
        return new ArtifactLease(context.LeaseKey);
    }

    public async Task TrimAsync(
        string sessionDirectory,
        long budgetBytes,
        CancellationToken cancellationToken = default)
    {
        var context = Context(sessionDirectory, "trim");
        var indexLock = IndexLocks.GetOrAdd(context.IndexPath, _ => new SemaphoreSlim(1, 1));
        await indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadAsync(context.IndexPath, cancellationToken).ConfigureAwait(false);
            TrimLocked(context, index, Math.Max(1, budgetBytes), protectedFingerprint: null);
            await SaveAsync(context.IndexPath, index, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            indexLock.Release();
        }
    }

    private static void TrimLocked(
        CacheContext context,
        CacheIndex index,
        long budgetBytes,
        string? protectedFingerprint)
    {
        foreach (var pair in index.Entries.ToArray())
        {
            var path = ResolveIndexedPath(context.SessionRoot, pair.Value.RelativePath);
            if (!IsValid(path, pair.Value.Length))
                index.Entries.Remove(pair.Key);
        }

        var total = index.Entries.Values.Sum(item => item.Length);
        foreach (var pair in index.Entries
                     .OrderBy(item => item.Value.LastAccessUtc)
                     .ToArray())
        {
            if (total <= budgetBytes) break;
            if (string.Equals(pair.Key, protectedFingerprint, StringComparison.Ordinal)
                || ActiveLeases.ContainsKey(LeaseKey(context.IndexPath, pair.Key)))
                continue;

            var path = ResolveIndexedPath(context.SessionRoot, pair.Value.RelativePath);
            if (!TryDelete(path)) continue;
            index.Entries.Remove(pair.Key);
            total -= pair.Value.Length;
        }
    }

    private static async Task<CacheIndex> LoadAsync(
        string indexPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(indexPath)) return new CacheIndex();
        try
        {
            await using var stream = new FileStream(
                indexPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var index = await JsonSerializer.DeserializeAsync<CacheIndex>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return index is { Version: SchemaVersion, Entries: not null }
                ? index
                : new CacheIndex();
        }
        catch (JsonException)
        {
            return new CacheIndex();
        }
        catch (IOException)
        {
            return new CacheIndex();
        }
    }

    private static async Task SaveAsync(
        string indexPath,
        CacheIndex index,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(indexPath)!;
        Directory.CreateDirectory(directory);
        var temporary = indexPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    index,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, indexPath, true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static CacheContext Context(string sessionDirectory, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var root = Path.GetFullPath(sessionDirectory);
        var indexPath = Path.Combine(root, "artifacts", IndexFileName);
        return new CacheContext(root, indexPath, LeaseKey(indexPath, fingerprint));
    }

    private static string ResolveIndexedPath(string sessionRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(sessionRoot, relativePath));
        EnsureInsideSession(sessionRoot, path);
        return path;
    }

    private static void EnsureInsideSession(string sessionRoot, string path)
    {
        var relative = Path.GetRelativePath(sessionRoot, path);
        if (relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new InvalidDataException("缓存路径越出了当前会话。");
    }

    private static bool IsValid(string path, long expectedLength)
    {
        try
        {
            return expectedLength > 0
                   && File.Exists(path)
                   && new FileInfo(path).Length == expectedLength;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static string LeaseKey(string indexPath, string fingerprint) =>
        $"{Path.GetFullPath(indexPath)}\n{fingerprint}";

    private static WMArtifactCacheEntry ToPublic(
        string fingerprint,
        string path,
        StoredEntry stored) =>
        new(fingerprint, path, stored.Length, stored.LastAccessUtc);

    private sealed class ArtifactLease(string key) : IDisposable
    {
        private string? activeKey = key;

        public void Dispose()
        {
            var value = Interlocked.Exchange(ref activeKey, null);
            if (value is null) return;
            while (ActiveLeases.TryGetValue(value, out var count))
            {
                if (count <= 1)
                {
                    if (((ICollection<KeyValuePair<string, int>>)ActiveLeases)
                        .Remove(new KeyValuePair<string, int>(value, count))) return;
                }
                else if (ActiveLeases.TryUpdate(value, count - 1, count))
                {
                    return;
                }
            }
        }
    }

    private sealed record StoredEntry(
        string RelativePath,
        long Length,
        DateTime LastAccessUtc);

    private sealed record CacheContext(
        string SessionRoot,
        string IndexPath,
        string LeaseKey);

    private sealed class CacheIndex
    {
        public int Version { get; set; } = SchemaVersion;
        public Dictionary<string, StoredEntry> Entries { get; set; } =
            new(StringComparer.Ordinal);
    }
}
