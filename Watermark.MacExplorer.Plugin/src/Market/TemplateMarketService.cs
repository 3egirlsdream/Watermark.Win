using Watermark.MacExplorer.Plugin.Accounts;
using Watermark.MacExplorer.Plugin.Library;
using Watermark.Shared.Models;

namespace Watermark.MacExplorer.Plugin.Market;

internal sealed record MarketItem(string Id, string Name, string UserId, int DownloadTimes, bool Recommend, string? Description, string? CoverPath);

internal sealed record MarketPage(IReadOnlyList<MarketItem> Items, int Cursor, bool HasMore);

internal sealed record DownloadOutcome(bool Succeeded, string Message);

/// <summary>简版模板市场：搜索、分页与下载入库。</summary>
internal sealed class TemplateMarketService
{
    public const int PageSize = 7;

    private readonly APIHelper api = new();

    public bool IsDownloaded(string templateId) =>
        File.Exists(Path.Combine(Global.AppPath.TemplatesFolder, templateId, "config.json"));

    public async Task<MarketPage> SearchAsync(string? keyword, int cursor, CancellationToken token)
    {
        var page = await api.GetMarketTemplatesAsync("recommended", keyword, cursor, PageSize, false, token).ConfigureAwait(false);
        var items = await Task.WhenAll(page.Items.Select(async item =>
        {
            var id = item.WatermarkId ?? string.Empty;
            var cover = await TemplateCoverCache.EnsureAsync(id, token).ConfigureAwait(false);
            return new MarketItem(id, string.IsNullOrWhiteSpace(item.Name) ? id : item.Name.Trim(),
                item.UserId ?? string.Empty, item.DownloadTimes, item.Recommend, item.Desc, cover);
        })).ConfigureAwait(false);
        return new MarketPage(items.Where(item => !string.IsNullOrWhiteSpace(item.Id)).ToArray(), page.NextCursor ?? 0, page.HasMore);
    }

    public async Task<DownloadOutcome> DownloadAsync(MarketItem item, CancellationToken token)
    {
        if (!AccountService.IsSignedIn) return new DownloadOutcome(false, "请先登录轻影账号后再下载模板。");
        var target = Path.Combine(Global.AppPath.TemplatesFolder, item.Id);
        try
        {
            if (!AccountService.IsMember)
            {
                var authorization = await api.DownloadTemplate(Global.CurrentUser.ID, item.Id).WaitAsync(token).ConfigureAwait(false);
                if (authorization?.success != true)
                    return new DownloadOutcome(false, authorization?.message?.content is { Length: > 0 } reason
                        ? reason
                        : "当前账号无法下载这个模板，开通会员后可下载全部模板。");
            }
            var downloaded = await api.Download(item.Id, item.UserId).WaitAsync(token).ConfigureAwait(false);
            if (!downloaded || !File.Exists(Path.Combine(target, "config.json")))
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                return new DownloadOutcome(false, "模板下载失败，请检查网络后重试。");
            }
            PluginLog.Info($"模板已下载：{item.Name}（{item.Id}）");
            return new DownloadOutcome(true, $"「{item.Name}」已加入左侧模板库。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"模板 {item.Id} 下载失败", ex);
            return new DownloadOutcome(false, "模板下载失败：" + ex.Message);
        }
    }
}
