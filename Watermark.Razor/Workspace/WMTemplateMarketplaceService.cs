#nullable enable

using Masa.Blazor;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Centralizes the market rules used by V2. Existing templates are backed up
/// until download completes so a network failure cannot destroy the local copy.
/// </summary>
public sealed class WMTemplateMarketplaceService(
    APIHelper api,
    IPopupService popupService,
    IClientInstance client,
    WMTemplateStore templateStore,
    WMTemplateLibraryService templateLibrary,
    IWMAccountService accounts) : IWMTemplateMarketplaceService
{
    private readonly object deletionGate = new();
    private readonly Dictionary<string, DeletedTemplate> pendingDeletions = new(StringComparer.Ordinal);
    private readonly WMTemplateMarketPager marketPager = new(new WMTemplateMarketApiPageSource(api));

    public async Task<WMTemplateMarketplacePageResult> SearchAsync(
        WMTemplateMarketplaceQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await marketPager.QueryAsync(query, cancellationToken).ConfigureAwait(false);
            foreach (var item in result.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Src)) item.Src = Global.GetSrc(item.WatermarkId);
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WMTemplateMarketplacePageResult(
                WMTemplateMarketplaceStatus.Failed,
                [],
                ex.Message);
        }
    }

    public async Task<WMTemplateMarketplacePageResult> GetFavoritesAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = Global.CurrentUser?.ID;
        if (string.IsNullOrWhiteSpace(userId))
            return new WMTemplateMarketplacePageResult(
                WMTemplateMarketplaceStatus.LoginRequired,
                [],
                "登录后可同步收藏模板。");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await api.GetILike(userId).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.success || response.data is null)
                return new WMTemplateMarketplacePageResult(
                    WMTemplateMarketplaceStatus.Failed,
                    [],
                    response.message?.content ?? "收藏列表加载失败。");
            foreach (var item in response.data) item.Src = Global.GetSrc(item.WatermarkId);
            return new WMTemplateMarketplacePageResult(
                WMTemplateMarketplaceStatus.Succeeded,
                response.data);
        }
        catch (Exception ex)
        {
            return new WMTemplateMarketplacePageResult(
                WMTemplateMarketplaceStatus.Failed,
                [],
                ex.Message);
        }
    }

    public async Task<WMTemplateMarketplaceResult> SetFavoriteAsync(
        WMZipedTemplate template,
        bool favorite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        var userId = Global.CurrentUser?.ID;
        if (string.IsNullOrWhiteSpace(userId))
            return new WMTemplateMarketplaceResult(
                WMTemplateMarketplaceStatus.LoginRequired,
                "登录后可收藏并同步市场模板。");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var success = favorite
                ? await api.AddILike(userId, template.WatermarkId).ConfigureAwait(false)
                : await api.DeleteILikeAsync(userId, template.WatermarkId).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return success
                ? new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Succeeded)
                : new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Failed, "收藏状态更新失败，请检查网络后重试。");
        }
        catch (Exception ex)
        {
            return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Failed, ex.Message);
        }
    }

    public async Task<WMTemplateMarketplaceResult> SetRecommendedAsync(
        WMZipedTemplate template,
        bool recommended,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        cancellationToken.ThrowIfCancellationRequested();
        if (!accounts.State.IsAuthenticated || !AdminAccessPolicy.IsAdmin(Global.CurrentUser))
            return new WMTemplateMarketplaceResult(
                WMTemplateMarketplaceStatus.Denied,
                "当前账号没有管理市场推荐的权限。");
        if (string.IsNullOrWhiteSpace(template.WatermarkId))
            return new WMTemplateMarketplaceResult(
                WMTemplateMarketplaceStatus.Failed,
                "模板标识无效。");
        if (template.Recommend == recommended)
            return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Succeeded);

        try
        {
            var response = await api.ToggleWatermarkRecommendationAsync(template.WatermarkId)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (response?.success != true)
                return new WMTemplateMarketplaceResult(
                    WMTemplateMarketplaceStatus.Failed,
                    response?.message?.content ?? "推荐状态更新失败，请稍后重试。");

            template.Recommend = recommended;
            return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Succeeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Failed, ex.Message);
        }
    }

    public async Task<WMTemplateMarketplaceResult> DownloadAsync(
        WMZipedTemplate template,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        cancellationToken.ThrowIfCancellationRequested();
        var user = Global.CurrentUser;
        if (user is null || string.IsNullOrWhiteSpace(user.ID))
            return new WMTemplateMarketplaceResult(
                WMTemplateMarketplaceStatus.LoginRequired,
                "登录后才能下载市场模板。");

        var templateId = template.WatermarkId;
        if (string.IsNullOrWhiteSpace(templateId))
            return new WMTemplateMarketplaceResult(
                WMTemplateMarketplaceStatus.Failed,
                "模板标识无效。");

        var target = Path.Combine(Global.AppPath.TemplatesFolder, templateId);
        if (Directory.Exists(target))
        {
            var confirmed = await popupService.ConfirmAsync(
                "确认覆盖",
                "本地已存在这个模板，是否用市场版本覆盖？",
                AlertTypes.Info).ConfigureAwait(false);
            if (confirmed != true)
                return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Cancelled);
        }

        if (!user.IsVIP)
        {
            var authorization = await api.DownloadTemplate(user.ID, templateId).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!authorization.success)
            {
                await client.ReLogin().ConfigureAwait(false);
                return new WMTemplateMarketplaceResult(
                    WMTemplateMarketplaceStatus.Denied,
                    authorization.message?.content ?? "当前账号无法下载这个模板。");
            }
        }

        string? backup = null;
        try
        {
            if (Directory.Exists(target))
            {
                var backupRoot = Path.Combine(Global.AppPath.BasePath, "Cache", "market-download-backups");
                Directory.CreateDirectory(backupRoot);
                backup = Path.Combine(backupRoot, $"{templateId}-{Guid.NewGuid():N}");
                Directory.Move(target, backup);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var downloaded = await api.Download(templateId, template.UserId ?? string.Empty).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!downloaded || !Directory.Exists(target))
                throw new IOException("模板下载失败，请检查网络后重试。");

            TryDeleteDirectory(backup);
            return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Succeeded);
        }
        catch (OperationCanceledException)
        {
            RestoreBackup(target, backup);
            throw;
        }
        catch (Exception ex)
        {
            RestoreBackup(target, backup);
            return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Failed, ex.Message);
        }
    }

    public async Task<WMLocalTemplateResult> CreateLocalAsync(
        IWMPhotoImportSource source,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var id = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var extension = Path.GetExtension(source.DisplayName).ToLowerInvariant();
        if (extension is not ".jpg" and not ".jpeg" and not ".png" and not ".webp") extension = ".jpg";
        var stagingRoot = Path.Combine(Global.AppPath.BasePath, "Cache", "template-imports");
        Directory.CreateDirectory(stagingRoot);
        var staging = Path.Combine(stagingRoot, $"{id}{extension}");
        try
        {
            await using (var input = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(
                             staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            var canvas = new WMCanvas
            {
                ID = id,
                Name = string.IsNullOrWhiteSpace(name) ? $"我的模板 {DateTime.Now:MM-dd}" : name.Trim(),
                Path = staging
            };
            canvas.Exif[canvas.ID] = new Dictionary<string, string>(ExifHelper.DefaultMeta);
            await templateStore.SaveAsync(canvas).ConfigureAwait(false);
            templateLibrary.Invalidate(id);
            return new WMLocalTemplateResult(WMTemplateMarketplaceStatus.Succeeded, id);
        }
        catch (Exception ex)
        {
            return new WMLocalTemplateResult(WMTemplateMarketplaceStatus.Failed, Message: ex.Message);
        }
        finally
        {
            TryDeleteFile(staging);
        }
    }

    public async Task<WMTemplateMarketplaceResult> UploadLocalAsync(
        WMTemplateUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var user = Global.CurrentUser;
        if (user is null || string.IsNullOrWhiteSpace(user.ID))
            return new WMTemplateMarketplaceResult(
                WMTemplateMarketplaceStatus.LoginRequired,
                "登录后才能上传模板到市场。");
        if (string.IsNullOrWhiteSpace(request.TemplateId))
            return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Failed, "模板标识无效。");
        if (string.IsNullOrWhiteSpace(request.Name))
            return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Failed, "请输入模板名称。");

        try
        {
            var ownership = await api.TemplateIsExsist(request.TemplateId, user.ID).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (ownership.Item1 && !ownership.Item2)
                return new WMTemplateMarketplaceResult(
                    WMTemplateMarketplaceStatus.Denied,
                    "该模板属于其他创作者，请另存为新模板后再上传。");
            if (ownership.Item1)
            {
                var confirmed = await popupService.ConfirmAsync(
                    "确认覆盖",
                    "该模板已上传，是否用当前本地版本覆盖市场版本？",
                    AlertTypes.Info).ConfigureAwait(false);
                if (confirmed != true)
                    return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Cancelled);
            }

            var result = await api.UploadWatermark(
                request.TemplateId,
                request.Name.Trim(),
                Math.Max(0, request.Coins),
                request.Description?.Trim() ?? string.Empty,
                request.Tags?.Trim() ?? string.Empty).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result?.success == true)
            {
                return new WMTemplateMarketplaceResult(
                    WMTemplateMarketplaceStatus.Succeeded,
                    result.data == false ? "模板已上传，但市场缓存刷新失败。" : "模板已上传到市场。");
            }

            return new WMTemplateMarketplaceResult(
                WMTemplateMarketplaceStatus.Failed,
                result?.message?.content ?? "模板上传失败，请稍后重试。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WMTemplateMarketplaceResult(WMTemplateMarketplaceStatus.Failed, ex.Message);
        }
        finally
        {
            TryDeleteFile(Path.Combine(Global.AppPath.TemplatesFolder, $"{request.TemplateId}.zip"));
        }
    }

    public async Task<WMTemplateUploadDefaults?> GetUploadDefaultsAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return null;
        var user = Global.CurrentUser;
        if (user is null || string.IsNullOrWhiteSpace(user.ID)) return null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await api.GetInfoById(templateId).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var template = result?.success == true ? result.data : null;
            if (template is null || !string.Equals(template.USER_ID, user.ID, StringComparison.Ordinal)) return null;
            return new WMTemplateUploadDefaults(
                template.NAME ?? string.Empty,
                template.DESC ?? string.Empty,
                Math.Max(0, template.COINS),
                template.TAGS ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Upload remains available with the local template values when its
            // previous market metadata cannot be retrieved.
            return null;
        }
    }

    public async Task<WMLocalTemplateResult> DeleteLocalAsync(
        WMTemplateList template,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        cancellationToken.ThrowIfCancellationRequested();
        var confirmed = await popupService.ConfirmAsync(
            "删除模板",
            $"确定删除“{template.Canvas?.Name ?? "这个模板"}”？可在当前页面撤销。",
            AlertTypes.Warning).ConfigureAwait(false);
        if (confirmed != true)
            return new WMLocalTemplateResult(WMTemplateMarketplaceStatus.Cancelled);
        var source = Path.Combine(Global.AppPath.TemplatesFolder, template.ID);
        if (!Directory.Exists(source))
            return new WMLocalTemplateResult(WMTemplateMarketplaceStatus.Failed, Message: "本地模板已不存在。");
        try
        {
            var trashRoot = Path.Combine(Global.AppPath.BasePath, "Cache", "template-trash");
            Directory.CreateDirectory(trashRoot);
            var target = Path.Combine(trashRoot, $"{template.ID}-{Guid.NewGuid():N}");
            Directory.Move(source, target);
            var token = Guid.NewGuid().ToString("N");
            lock (deletionGate) pendingDeletions[token] = new DeletedTemplate(template.ID, source, target);
            templateLibrary.Invalidate(template.ID);
            return new WMLocalTemplateResult(
                WMTemplateMarketplaceStatus.Succeeded,
                template.ID,
                token);
        }
        catch (Exception ex)
        {
            return new WMLocalTemplateResult(WMTemplateMarketplaceStatus.Failed, Message: ex.Message);
        }
    }

    public Task<WMLocalTemplateResult> UndoDeleteLocalAsync(
        string undoToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeletedTemplate? item;
        lock (deletionGate)
        {
            if (!pendingDeletions.Remove(undoToken, out item))
                return Task.FromResult(new WMLocalTemplateResult(
                    WMTemplateMarketplaceStatus.Failed,
                    Message: "撤销操作已过期。"));
        }
        try
        {
            if (!Directory.Exists(item.TrashPath))
                throw new DirectoryNotFoundException("回收区中的模板已不存在。");
            if (Directory.Exists(item.OriginalPath)) Directory.Delete(item.OriginalPath, true);
            Directory.Move(item.TrashPath, item.OriginalPath);
            templateLibrary.Invalidate(item.TemplateId);
            return Task.FromResult(new WMLocalTemplateResult(
                WMTemplateMarketplaceStatus.Succeeded,
                item.TemplateId));
        }
        catch (Exception ex)
        {
            lock (deletionGate) pendingDeletions[undoToken] = item;
            return Task.FromResult(new WMLocalTemplateResult(
                WMTemplateMarketplaceStatus.Failed,
                Message: ex.Message));
        }
    }

    private static void RestoreBackup(string target, string? backup)
    {
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            if (!string.IsNullOrWhiteSpace(backup) && Directory.Exists(backup))
                Directory.Move(backup, target);
        }
        catch
        {
            // Preserve the backup for manual recovery if the atomic rename fails.
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record DeletedTemplate(string TemplateId, string OriginalPath, string TrashPath);
}
