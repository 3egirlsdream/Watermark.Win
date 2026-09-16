using MacExplorer.PluginSdk;
using Watermark.Shared.Models;

namespace Watermark.MacExplorer.Plugin.Access;

/// <summary>
/// 授权状态机：未登录先登录，登录后交给宿主的 7 天试用账本，试用到期则要求开通会员。
/// </summary>
internal sealed class PluginAccessService
{
    public async Task<PluginAccessResult> CheckAsync(PluginAccessRequest request, CancellationToken cancellationToken)
    {
        PluginEnvironment.EnsureInitialized();
        try
        {
            var credentials = await Global.ReadLocalAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(credentials.Item1))
                return new PluginAccessResult(PluginAccessStatus.LoginRequired, "请先登录轻影账号，登录后可开启 7 天免费试用。");

            var api = new APIHelper();
            var login = await api.LoginIn(credentials.Item1, credentials.Item2, true).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (login?.success != true || login.data?.data is null)
                return new PluginAccessResult(PluginAccessStatus.Failed, "无法验证轻影账号状态：" + FailureReason(login));

            Global.CurrentUser = Global.SetUserInfo(login.data.data);
            if (Global.CurrentUser.IsVIP)
                return new PluginAccessResult(PluginAccessStatus.Allowed, $"会员有效期至 {Global.CurrentUser.EXPIRE_DATE:yyyy-MM-dd}。");

            if (request.Trial.StartedAt is null)
                return new PluginAccessResult(PluginAccessStatus.TrialAvailable, "已登录轻影账号，可开始 7 天免费试用。");
            if (!request.Trial.Active)
                return new PluginAccessResult(PluginAccessStatus.TrialExpired, "7 天免费试用已结束，开通会员后继续使用。");
            return new PluginAccessResult(PluginAccessStatus.TrialAvailable, "试用进行中。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PluginLog.Error("授权检查失败", ex);
            return new PluginAccessResult(PluginAccessStatus.Failed, "无法验证轻影账号状态：" + ex.Message);
        }
    }

    private static string FailureReason(API<WMLoginModel>? login) =>
        login?.message?.content is { Length: > 0 } content ? content
        : login?.data?.Message is { Length: > 0 } message ? message
        : "网络异常或登录状态已失效。";
}
