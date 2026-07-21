#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public interface IWMTemplateMarketPageSource
{
    Task<WMTemplateMarketPage> GetAsync(
        WMTemplateMarketCategory category,
        string keyword,
        int cursor,
        int pageSize,
        bool forceRefresh,
        CancellationToken cancellationToken = default);
}

public sealed class WMTemplateMarketApiPageSource(APIHelper api) : IWMTemplateMarketPageSource
{
    public Task<WMTemplateMarketPage> GetAsync(
        WMTemplateMarketCategory category,
        string keyword,
        int cursor,
        int pageSize,
        bool forceRefresh,
        CancellationToken cancellationToken = default) =>
        api.GetMarketTemplatesAsync(
            category.ToString().ToLowerInvariant(),
            keyword,
            cursor,
            pageSize,
            forceRefresh,
            cancellationToken);
}

public sealed class WMTemplateMarketPager(IWMTemplateMarketPageSource source)
{
    public async Task<WMTemplateMarketplacePageResult> QueryAsync(
        WMTemplateMarketplaceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var cursor = Math.Max(0, query.Start);
        var pageSize = Math.Clamp(query.PageSize, 1, 40);
        cancellationToken.ThrowIfCancellationRequested();
        var page = await source.GetAsync(
            query.Category,
            query.Keyword.Trim(),
            cursor,
            pageSize,
            query.ForceRefresh,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var items = page.Items
            .Where(item => item.Visible && !string.IsNullOrWhiteSpace(item.WatermarkId))
            .DistinctBy(item => item.WatermarkId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var nextCursor = page.NextCursor ?? cursor + items.Length;

        return new WMTemplateMarketplacePageResult(
            WMTemplateMarketplaceStatus.Succeeded,
            items,
            NextStart: nextCursor,
            HasMore: page.HasMore,
            SourceRequestCount: 1);
    }
}

public static class WMTemplateMarketFeatureSelector
{
    public static WMZipedTemplate? Pick(
        IReadOnlyList<WMZipedTemplate> items,
        string keyword,
        Random? random = null)
    {
        if (!string.IsNullOrWhiteSpace(keyword)) return null;
        var candidates = items
            .Where(item => !string.IsNullOrWhiteSpace(item.WatermarkId)
                && !string.IsNullOrWhiteSpace(item.Src))
            .ToArray();
        if (candidates.Length == 0) return null;
        random ??= Random.Shared;
        return candidates[random.Next(candidates.Length)];
    }

    public static List<WMZipedTemplate> WithoutFeatured(
        IReadOnlyList<WMZipedTemplate> items,
        WMZipedTemplate? featuredItem) =>
        featuredItem is null
            ? items.ToList()
            : items.Where(item => !string.Equals(
                    item.WatermarkId,
                    featuredItem.WatermarkId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
}

public sealed class WMTemplateMarketFeedStore
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private const int MaximumCachedFeeds = 12;
    private readonly Dictionary<WMTemplateMarketFeedKey, WMTemplateMarketFeedState> feeds = [];
    private readonly HashSet<string> downloadedIds = new(StringComparer.OrdinalIgnoreCase);

    public WMTemplateMarketCategory SelectedCategory { get; set; } = WMTemplateMarketCategory.Recommended;
    public string Search { get; set; } = string.Empty;

    public WMTemplateMarketFeedState GetOrCreate(
        WMTemplateMarketCategory category,
        string keyword,
        DateTimeOffset? now = null)
    {
        var normalizedKeyword = keyword.Trim();
        var key = new WMTemplateMarketFeedKey(category, normalizedKeyword.ToUpperInvariant());
        var utcNow = now ?? DateTimeOffset.UtcNow;
        if (feeds.TryGetValue(key, out var existing))
        {
            if (existing.Initialized && utcNow - existing.LastLoadedAtUtc > CacheDuration)
                existing.Reset();
            existing.LastAccessedAtUtc = utcNow;
            return existing;
        }

        var state = new WMTemplateMarketFeedState(category, normalizedKeyword)
        {
            LastAccessedAtUtc = utcNow
        };
        feeds[key] = state;
        Prune(key);
        return state;
    }

    public void MarkDownloaded(string templateId)
    {
        if (!string.IsNullOrWhiteSpace(templateId)) downloadedIds.Add(templateId);
    }

    public bool IsDownloaded(string templateId) =>
        !string.IsNullOrWhiteSpace(templateId) && downloadedIds.Contains(templateId);

    public void SetRecommended(string templateId, bool recommended)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return;
        foreach (var state in feeds.Values)
        {
            foreach (var item in state.Items.Where(item => string.Equals(
                         item.WatermarkId,
                         templateId,
                         StringComparison.OrdinalIgnoreCase)))
            {
                item.Recommend = recommended;
            }

            if (recommended || state.Category != WMTemplateMarketCategory.Recommended) continue;
            var removed = state.Items.RemoveAll(item => string.Equals(
                item.WatermarkId,
                templateId,
                StringComparison.OrdinalIgnoreCase));
            if (removed > 0) state.NextStart = Math.Max(0, state.NextStart - removed);
        }
    }

    private void Prune(WMTemplateMarketFeedKey current)
    {
        while (feeds.Count > MaximumCachedFeeds)
        {
            var oldest = feeds
                .Where(pair => pair.Key != current)
                .OrderBy(pair => pair.Value.LastAccessedAtUtc)
                .First();
            feeds.Remove(oldest.Key);
        }
    }
}

public sealed class WMTemplateMarketFeedState(
    WMTemplateMarketCategory category,
    string keyword)
{
    public WMTemplateMarketCategory Category { get; } = category;
    public string Keyword { get; } = keyword;
    public List<WMZipedTemplate> Items { get; } = [];
    public int NextStart { get; set; }
    public bool HasMore { get; set; } = true;
    public bool Initialized { get; set; }
    public bool IsLoading { get; set; }
    public string? ErrorMessage { get; set; }
    public double ScrollTop { get; set; }
    public DateTimeOffset LastLoadedAtUtc { get; set; }
    public DateTimeOffset LastAccessedAtUtc { get; set; }
    private long loadVersion;

    public long BeginLoad()
    {
        IsLoading = true;
        return ++loadVersion;
    }

    public bool IsCurrentLoad(long version) => version == loadVersion;

    public void CompleteLoad(long version)
    {
        if (IsCurrentLoad(version)) IsLoading = false;
    }

    public void InvalidatePendingLoad()
    {
        loadVersion++;
        IsLoading = false;
    }

    public int AppendUnique(IEnumerable<WMZipedTemplate> items)
    {
        var existing = Items
            .Select(item => item.WatermarkId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.WatermarkId) || !existing.Add(item.WatermarkId)) continue;
            Items.Add(item);
            added++;
        }
        return added;
    }

    public void Reset()
    {
        InvalidatePendingLoad();
        Items.Clear();
        NextStart = 0;
        HasMore = true;
        Initialized = false;
        ErrorMessage = null;
        ScrollTop = 0;
        LastLoadedAtUtc = default;
    }
}

public readonly record struct WMTemplateMarketFeedKey(
    WMTemplateMarketCategory Category,
    string Keyword);
