using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMTemplateMarketPagingTests
{
    [Fact]
    public async Task Query_ForwardsExactFilter_AndUsesOneApiRequest()
    {
        var source = new FakeSource((category, keyword, cursor, pageSize) => new WMTemplateMarketPage
        {
            Items = Enumerable.Range(cursor, pageSize).Select(index => Template(index)).ToList(),
            NextCursor = cursor + pageSize,
            HasMore = true,
            Category = category.ToString().ToLowerInvariant()
        });
        var pager = new WMTemplateMarketPager(source);

        var result = await pager.QueryAsync(new WMTemplateMarketplaceQuery(
            WMTemplateMarketCategory.Collage,
            " 旅行 ",
            40,
            20));

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Items.Count);
        Assert.Equal(60, result.NextStart);
        Assert.True(result.HasMore);
        Assert.Equal(1, result.SourceRequestCount);
        var request = Assert.Single(source.Requests);
        Assert.Equal(WMTemplateMarketCategory.Collage, request.Category);
        Assert.Equal("旅行", request.Keyword);
        Assert.Equal(40, request.Cursor);
        Assert.Equal(20, request.PageSize);
    }

    [Fact]
    public async Task Query_DoesNotScanMorePages_WhenCategoryPageIsShort()
    {
        var source = new FakeSource((category, keyword, cursor, pageSize) => new WMTemplateMarketPage
        {
            Items = [Template(1), Template(2), Template(3)],
            NextCursor = null,
            HasMore = false,
            Category = "recommended"
        });
        var pager = new WMTemplateMarketPager(source);

        var result = await pager.QueryAsync(new WMTemplateMarketplaceQuery(
            WMTemplateMarketCategory.Recommended,
            string.Empty,
            0,
            20));

        Assert.Equal(3, result.Items.Count);
        Assert.False(result.HasMore);
        Assert.Equal(3, result.NextStart);
        Assert.Equal(1, result.SourceRequestCount);
        Assert.Single(source.Requests);
    }

    [Fact]
    public async Task Query_DeduplicatesMalformedApiPage_WithoutExtraRequest()
    {
        var duplicate = Template(7);
        var source = new FakeSource((category, keyword, cursor, pageSize) => new WMTemplateMarketPage
        {
            Items = [duplicate, duplicate, Template(8), Template(9, visible: false)],
            NextCursor = 4,
            HasMore = true
        });
        var pager = new WMTemplateMarketPager(source);

        var result = await pager.QueryAsync(new WMTemplateMarketplaceQuery(
            WMTemplateMarketCategory.Popular,
            string.Empty,
            0,
            20));

        Assert.Equal(["7", "8"], result.Items.Select(item => item.WatermarkId));
        Assert.Equal(4, result.NextStart);
        Assert.Single(source.Requests);
    }

    [Fact]
    public async Task CancelledQuery_CannotCommitAStalePage()
    {
        var source = new BlockingSource();
        var pager = new WMTemplateMarketPager(source);
        using var cancellation = new CancellationTokenSource();
        var query = pager.QueryAsync(new WMTemplateMarketplaceQuery(
            WMTemplateMarketCategory.Recommended,
            string.Empty,
            0,
            20), cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public void FeedStore_ReusesFreshState_Deduplicates_AndExpiresOldResults()
    {
        var store = new WMTemplateMarketFeedStore();
        var now = DateTimeOffset.UtcNow;
        var state = store.GetOrCreate(WMTemplateMarketCategory.Latest, "胶片", now);
        state.AppendUnique([Template(1), Template(1), Template(2)]);
        state.Initialized = true;
        state.LastLoadedAtUtc = now;
        state.ScrollTop = 420;

        var cached = store.GetOrCreate(WMTemplateMarketCategory.Latest, "胶片", now.AddMinutes(9));

        Assert.Same(state, cached);
        Assert.Equal(2, cached.Items.Count);

        var expired = store.GetOrCreate(WMTemplateMarketCategory.Latest, "胶片", now.AddMinutes(11));

        Assert.Same(state, expired);
        Assert.Empty(expired.Items);
        Assert.False(expired.Initialized);
        Assert.Equal(0, expired.ScrollTop);
    }

    [Fact]
    public void FeedState_OldCompletionCannotFinishTheLatestLoad()
    {
        var state = new WMTemplateMarketFeedState(WMTemplateMarketCategory.Recommended, string.Empty);
        var oldLoad = state.BeginLoad();
        state.InvalidatePendingLoad();
        var latestLoad = state.BeginLoad();

        state.CompleteLoad(oldLoad);

        Assert.True(state.IsLoading);

        state.CompleteLoad(latestLoad);

        Assert.False(state.IsLoading);
    }

    [Fact]
    public void FeatureSelector_PicksAnImage_AndRemovesItFromTheGrid()
    {
        var items = new[]
        {
            Template(1),
            Template(2),
            Template(3)
        };
        foreach (var item in items) item.Src = $"https://images.example/{item.WatermarkId}.jpg";

        var featured = WMTemplateMarketFeatureSelector.Pick(items, string.Empty, new Random(7));
        var grid = WMTemplateMarketFeatureSelector.WithoutFeatured(items, featured);

        Assert.NotNull(featured);
        Assert.Equal(2, grid.Count);
        Assert.DoesNotContain(grid, item => item.WatermarkId == featured!.WatermarkId);
        Assert.Null(WMTemplateMarketFeatureSelector.Pick(items, "旅行", new Random(7)));
    }

    private static WMZipedTemplate Template(int index, bool visible = true) => new()
    {
        WatermarkId = index.ToString(),
        Name = $"模板 {index}",
        Desc = string.Empty,
        UserDisplayName = "轻影创作者",
        Visible = visible,
        Tags = []
    };

    private sealed class FakeSource(
        Func<WMTemplateMarketCategory, string, int, int, WMTemplateMarketPage> responseFactory)
        : IWMTemplateMarketPageSource
    {
        public List<Request> Requests { get; } = [];

        public Task<WMTemplateMarketPage> GetAsync(
            WMTemplateMarketCategory category,
            string keyword,
            int cursor,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new Request(category, keyword, cursor, pageSize));
            return Task.FromResult(responseFactory(category, keyword, cursor, pageSize));
        }
    }

    private sealed class BlockingSource : IWMTemplateMarketPageSource
    {
        public int CallCount { get; private set; }

        public async Task<WMTemplateMarketPage> GetAsync(
            WMTemplateMarketCategory category,
            string keyword,
            int cursor,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new WMTemplateMarketPage();
        }
    }

    private sealed record Request(
        WMTemplateMarketCategory Category,
        string Keyword,
        int Cursor,
        int PageSize);
}
