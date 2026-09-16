using System.Diagnostics;
using Watermark.Shared.Models;

namespace Watermark.MacExplorer.Plugin.Accounts;

internal sealed record MembershipPlan(string Id, string Name, decimal Price, string Description, bool Recommended = false);

internal sealed record AccountResult(bool Succeeded, string Message);

/// <summary>轻影账号与会员，复用各端共用的账号服务与桌面支付流程。</summary>
internal sealed class AccountService
{
    public const string WebsiteUrl = "https://thankful.top/";

    public static readonly MembershipPlan[] Plans =
    [
        new("year", "年度会员", 28, "适合长期批量处理", true),
        new("quarter", "季度会员", 18, "适合阶段性创作"),
        new("month", "月度会员", 8, "轻量体验高级能力")
    ];

    private static readonly TimeSpan[] QueryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(4)
    ];

    private readonly APIHelper api = new();

    public static bool IsSignedIn => !string.IsNullOrWhiteSpace(Global.CurrentUser?.ID);

    public static bool IsMember => IsSignedIn && Global.CurrentUser.IsVIP;

    public static string DisplayName =>
        !string.IsNullOrWhiteSpace(Global.CurrentUser?.DISPLAY_NAME) ? Global.CurrentUser.DISPLAY_NAME
        : !string.IsNullOrWhiteSpace(Global.CurrentUser?.USER_NAME) ? Global.CurrentUser.USER_NAME
        : "轻影账号";

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        var credentials = await Global.ReadLocalAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(credentials.Item1)) return false;
        return await RefreshAsync(credentials.Item1, credentials.Item2, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return new AccountResult(false, "请输入邮箱和密码。");
        try
        {
            var login = await api.LoginIn(email.Trim(), password)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            if (login?.success != true || login.data?.data is null)
                return new AccountResult(false, "登录失败：" + FailureReason(login));
            Global.CurrentUser = Global.SetUserInfo(login.data.data);
            await Global.WriteAccount2LocalAsync(email.Trim(), api.GetMD5(password)).ConfigureAwait(false);
            PluginLog.Info($"轻影账号已登录：{Global.CurrentUser.USER_NAME}");
            return new AccountResult(true, IsMember ? "登录成功，会员已生效。" : "登录成功。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PluginLog.Error("登录失败", ex);
            return new AccountResult(false, "登录失败：" + ex.Message);
        }
    }

    public Task SignOutAsync()
    {
        Global.CurrentUser = new WMLoginChildModel();
        return Global.WriteAccount2LocalAsync(string.Empty, string.Empty);
    }

    public async Task<AccountResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var credentials = await Global.ReadLocalAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(credentials.Item1))
            return new AccountResult(false, "尚未登录轻影账号。");
        var refreshed = await RefreshAsync(credentials.Item1, credentials.Item2, cancellationToken).ConfigureAwait(false);
        return refreshed
            ? new AccountResult(true, IsMember ? $"会员有效期至 {Global.CurrentUser.EXPIRE_DATE:yyyy-MM-dd}。" : "账号状态已更新，当前未开通会员。")
            : new AccountResult(false, "无法刷新账号状态，请检查网络后重试。");
    }

    /// <summary>创建桌面支付订单并轮询支付结果；支付成功后回读账号状态。</summary>
    public async Task<AccountResult> PurchaseAsync(
        MembershipPlan plan,
        string outTradeNo,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (!IsSignedIn) return new AccountResult(false, "请先登录轻影账号。");
        try
        {
            if (string.IsNullOrWhiteSpace(outTradeNo))
            {
                status?.Report("正在创建支付订单…");
                var order = await api.CreateDesktopPay(plan.Price, plan.Name, Global.CurrentUser.ID)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                if (order?.success != true || order.data is null || string.IsNullOrWhiteSpace(order.data.PayUrl))
                    return new AccountResult(false, order?.message?.content ?? "暂时无法创建支付订单，请稍后重试。");
                outTradeNo = order.data.OutTradeNo;
                var payUrl = ResolvePayPageUrl(order.data.PayUrl);
                if (!OpenUrl(payUrl))
                    return new AccountResult(false, $"无法打开支付页面，请手动访问：{payUrl}");
                PluginLog.Info($"已创建会员订单 {outTradeNo}（{plan.Name} ¥{plan.Price}），支付页：{payUrl}");
            }

            foreach (var delay in QueryDelays)
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                status?.Report("等待支付结果…");
                var paid = await TryQueryAsync(outTradeNo, cancellationToken).ConfigureAwait(false);
                if (paid is null) continue;
                if (paid.Succeeded || paid.Final) return paid.Result;
            }
            return new AccountResult(false, $"尚未收到支付结果，订单号 {outTradeNo}。支付完成后可再次查询。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PluginLog.Error("开通会员失败", ex);
            return new AccountResult(false, "开通会员失败：" + ex.Message);
        }
    }

    private sealed record PaymentState(bool Succeeded, bool Final, AccountResult Result);

    private async Task<PaymentState?> TryQueryAsync(string outTradeNo, CancellationToken cancellationToken)
    {
        try
        {
            var result = await api.QueryPay(outTradeNo).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result?.success != true || result.data is null) return null;
            var paid = string.Equals(result.data.Status, "PAID", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(result.data.Status, "TRADE_SUCCESS", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(result.data.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
                       || result.data.ExpireDate > DateTime.Now;
            if (paid)
            {
                var refreshed = await RefreshAsync(cancellationToken).ConfigureAwait(false);
                return new PaymentState(true, true, new AccountResult(true,
                    IsMember
                        ? $"支付成功，会员有效期至 {Global.CurrentUser.EXPIRE_DATE:yyyy-MM-dd}。"
                        : "支付成功，正在等待会员生效，请稍后重新点击菜单确认。"));
            }
            if (string.Equals(result.data.Status, "CLOSED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.data.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
                return new PaymentState(false, true, new AccountResult(false, result.data.Message ?? "支付未完成，请重新下单。"));
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PluginLog.Warning("查询支付状态失败：" + ex.Message);
            return null;
        }
    }

    private async Task<bool> RefreshAsync(string user, string password, CancellationToken cancellationToken)
    {
        var login = await api.LoginIn(user, password, true).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (login?.success != true || login.data?.data is null) return false;
        Global.CurrentUser = Global.SetUserInfo(login.data.data);
        return true;
    }

    private static string FailureReason(API<WMLoginModel>? login) =>
        login?.message?.content is { Length: > 0 } content ? content
        : login?.data?.Message is { Length: > 0 } message ? message
        : "网络异常，请稍后重试。";

    public static bool OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("open") { ArgumentList = { uri.AbsoluteUri } });
            return true;
        }
        catch (Exception ex)
        {
            PluginLog.Error("打开链接失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 服务端按内部地址（如 http://thankful.top:4396）拼支付页地址，而域名带 HSTS，
    /// 浏览器会把 http 升级成 4396 端口上的 https 直接连接失败，这里统一改写到公共 HTTPS 入口。
    /// </summary>
    private static string ResolvePayPageUrl(string payUrl)
    {
        if (!Uri.TryCreate(payUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || (uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443))
            return payUrl;
        var resolved = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.AbsoluteUri;
        PluginLog.Info($"支付页地址改用公共入口：{payUrl} → {resolved}");
        return resolved;
    }
}
