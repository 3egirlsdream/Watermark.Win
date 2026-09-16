#nullable enable

using Watermark.Razor.Workspace;
using Watermark.Shared.Models;

namespace Watermark.Web.Services;

/// <summary>
/// 官网只开放注册流程：直接调用与客户端相同的账号接口，
/// 但不写本机账号文件、也不使用进程级登录态，避免访客之间互相串号。
/// </summary>
public sealed class WMWebAccountService(APIHelper api) : IWMAccountService
{
    private WMAccountState state = Unauthenticated;

    public WMAccountState State => state;
    public event Action? Changed;

    public Task RefreshAsync(CancellationToken token = default) => Task.CompletedTask;

    public async Task<WMAccountResult> RegisterAsync(WMRegisterRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!WMAccountService.IsEmail(request.Email)) return new WMAccountResult(false, "邮箱格式不正确。");
        if (request.Password.Length < 9) return new WMAccountResult(false, "密码至少需要9个字符。");
        if (string.IsNullOrWhiteSpace(request.DisplayName)) return new WMAccountResult(false, "请输入展示名称。");
        if (string.IsNullOrWhiteSpace(request.VerificationCode)) return new WMAccountResult(false, "请输入验证码。");
        try
        {
            var result = await api.Register(new WMSysUser
            {
                USER_NAME = request.Email.Trim(),
                DISPLAY_NAME = request.DisplayName.Trim(),
                PASSWORD = request.Password,
                PK_ID = Guid.NewGuid().ToString("N"),
                Code = request.VerificationCode.Trim()
            }).ConfigureAwait(false);
            if (result?.success != true || result.data is null)
                return new WMAccountResult(false, Message(result?.message?.content, "注册失败。"));

            state = new WMAccountState(
                true,
                result.data.ID,
                string.IsNullOrWhiteSpace(result.data.DISPLAY_NAME) ? "轻影用户" : result.data.DISPLAY_NAME,
                result.data.USER_NAME,
                null,
                result.data.EXPIRE_DATE > DateTime.Now,
                result.data.COINS,
                result.data.EXPIRE_DATE);
            Changed?.Invoke();
            return new WMAccountResult(true, "注册成功。");
        }
        catch (Exception ex)
        {
            return new WMAccountResult(false, Message(ex.Message, "注册失败，请稍后重试。"));
        }
    }

    public async Task<WMAccountResult> SendVerificationCodeAsync(string email, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!WMAccountService.IsEmail(email)) return new WMAccountResult(false, "邮箱格式不正确。");
        try
        {
            var result = await api.SendMail(email.Trim()).ConfigureAwait(false);
            return result?.success == true
                ? new WMAccountResult(true, "验证码已发送，请检查邮箱和垃圾邮件目录。")
                : new WMAccountResult(false, Message(result?.message?.content, "验证码发送失败。"));
        }
        catch (Exception ex)
        {
            return new WMAccountResult(false, Message(ex.Message, "验证码发送失败。"));
        }
    }

    public Task<WMAccountResult> LoginAsync(WMLoginRequest request, CancellationToken token) =>
        Task.FromResult(new WMAccountResult(false, "官网暂不支持登录，请在轻影客户端登录。"));

    public Task<WMAccountResult> RecoverPasswordAsync(WMRecoverPasswordRequest request, CancellationToken token) =>
        Task.FromResult(new WMAccountResult(false, "官网暂不支持找回密码，请在轻影客户端操作。"));

    public Task<WMAccountResult> ChangePasswordAsync(WMChangePasswordRequest request, CancellationToken token) =>
        Task.FromResult(new WMAccountResult(false, "官网暂不支持修改密码，请在轻影客户端操作。"));

    public Task<WMAccountResult> DeleteAccountAsync(WMDeleteAccountRequest request, CancellationToken token) =>
        Task.FromResult(new WMAccountResult(false, "官网暂不支持删除账号，请在轻影客户端操作。"));

    public Task SignOutAsync()
    {
        state = Unauthenticated;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private static string Message(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static readonly WMAccountState Unauthenticated =
        new(false, null, "轻影用户", null, null, false, 0, null);
}
