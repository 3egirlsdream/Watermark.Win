#nullable enable

using System.Text;
using SkiaSharp;
using Watermark.Shared.Enums;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed record WMAccountState(
    bool IsAuthenticated,
    string? UserId,
    string DisplayName,
    string? UserName,
    string? AvatarUrl,
    bool IsVip,
    int Coins,
    DateTime? ExpiresAt);

public sealed record WMAccountResult(bool Succeeded, string? Message = null);
public sealed record WMLoginRequest(string Email, string Password);
public sealed record WMRegisterRequest(
    string Email,
    string DisplayName,
    string Password,
    string VerificationCode);
public sealed record WMRecoverPasswordRequest(
    string Email,
    string NewPassword,
    string VerificationCode);
public sealed record WMChangePasswordRequest(
    string Email,
    string OldPassword,
    string NewPassword);
public sealed record WMDeleteAccountRequest(
    string Email,
    string Password,
    string VerificationCode);

public interface IWMAccountService
{
    WMAccountState State { get; }
    event Action? Changed;
    Task RefreshAsync(CancellationToken token = default);
    Task<WMAccountResult> LoginAsync(WMLoginRequest request, CancellationToken token);
    Task<WMAccountResult> RegisterAsync(WMRegisterRequest request, CancellationToken token);
    Task<WMAccountResult> RecoverPasswordAsync(WMRecoverPasswordRequest request, CancellationToken token);
    Task<WMAccountResult> ChangePasswordAsync(WMChangePasswordRequest request, CancellationToken token);
    Task<WMAccountResult> DeleteAccountAsync(WMDeleteAccountRequest request, CancellationToken token);
    Task<WMAccountResult> SendVerificationCodeAsync(string email, CancellationToken token);
    Task SignOutAsync();
}

public sealed class WMAccountService(
    APIHelper api,
    IClientInstance client,
    IWMWorkspaceTraceStore? traces = null) : IWMAccountService
{
    public event Action? Changed;
    public WMAccountState State => Project(Global.CurrentUser);

    public async Task RefreshAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            await Global.Login().ConfigureAwait(false);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            await LogAsync("account-refresh-failed", WMDiagnosticLogLevel.Warning, ex.Message, ex).ConfigureAwait(false);
        }
    }

    public async Task<WMAccountResult> LoginAsync(WMLoginRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return new WMAccountResult(false, "请输入邮箱和密码。");
        try
        {
            var result = await api.LoginIn(request.Email.Trim(), request.Password).ConfigureAwait(false);
            if (result?.success != true || result.data?.data is null)
                return await FailureAsync("account-login-failed", result?.message?.content ?? result?.data?.Message ?? "登录失败。").ConfigureAwait(false);
            Global.CurrentUser = Global.SetUserInfo(result.data.data);
            await Global.WriteAccount2LocalAsync(request.Email.Trim(), api.GetMD5(request.Password)).ConfigureAwait(false);
            Changed?.Invoke();
            await LogAsync("account-login-completed", WMDiagnosticLogLevel.Information, "账号登录成功。").ConfigureAwait(false);
            return new WMAccountResult(true, "登录成功。");
        }
        catch (Exception ex)
        {
            return await FailureAsync("account-login-failed", ex.Message, ex).ConfigureAwait(false);
        }
    }

    public async Task<WMAccountResult> RegisterAsync(WMRegisterRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var validation = ValidateEmailAndPassword(request.Email, request.Password);
        if (validation is not null) return new WMAccountResult(false, validation);
        if (string.IsNullOrWhiteSpace(request.DisplayName)) return new WMAccountResult(false, "请输入展示名称。");
        if (string.IsNullOrWhiteSpace(request.VerificationCode)) return new WMAccountResult(false, "请输入验证码。");
        try
        {
            EnsurePrimaryKey();
            var result = await api.Register(new WMSysUser
            {
                USER_NAME = request.Email.Trim(),
                DISPLAY_NAME = request.DisplayName.Trim(),
                PASSWORD = request.Password,
                PK_ID = Global.PrimaryKey,
                Code = request.VerificationCode.Trim()
            }).ConfigureAwait(false);
            if (result?.success != true || result.data is null)
                return await FailureAsync("account-register-failed", result?.message?.content ?? "注册失败。").ConfigureAwait(false);
            Global.CurrentUser = FromUser(result.data);
            await Global.WriteAccount2LocalAsync(request.Email.Trim(), api.GetMD5(request.Password)).ConfigureAwait(false);
            Changed?.Invoke();
            await LogAsync("account-register-completed", WMDiagnosticLogLevel.Information, "账号注册成功。").ConfigureAwait(false);
            return new WMAccountResult(true, "注册并登录成功。");
        }
        catch (Exception ex)
        {
            return await FailureAsync("account-register-failed", ex.Message, ex).ConfigureAwait(false);
        }
    }

    public Task<WMAccountResult> RecoverPasswordAsync(WMRecoverPasswordRequest request, CancellationToken token) =>
        UpdatePasswordAsync(request.Email, string.Empty, request.NewPassword, request.VerificationCode, true, token);

    public Task<WMAccountResult> ChangePasswordAsync(WMChangePasswordRequest request, CancellationToken token) =>
        UpdatePasswordAsync(request.Email, request.OldPassword, request.NewPassword, string.Empty, false, token);

    public async Task<WMAccountResult> DeleteAccountAsync(WMDeleteAccountRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password)
            || string.IsNullOrWhiteSpace(request.VerificationCode))
            return new WMAccountResult(false, "请完整填写邮箱、密码和验证码。");
        try
        {
            var result = await api.DeleteAccount(
                request.Email.Trim(), request.Password, request.VerificationCode.Trim()).ConfigureAwait(false);
            if (result?.success != true)
                return await FailureAsync("account-delete-failed", result?.message?.content ?? "账号删除失败。").ConfigureAwait(false);
            await SignOutAsync().ConfigureAwait(false);
            await LogAsync("account-delete-completed", WMDiagnosticLogLevel.Information, "账号已删除。").ConfigureAwait(false);
            return new WMAccountResult(true, "账号已删除。");
        }
        catch (Exception ex)
        {
            return await FailureAsync("account-delete-failed", ex.Message, ex).ConfigureAwait(false);
        }
    }

    public async Task<WMAccountResult> SendVerificationCodeAsync(string email, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsEmail(email)) return new WMAccountResult(false, "邮箱格式不正确。");
        try
        {
            var result = await api.SendMail(email.Trim()).ConfigureAwait(false);
            return result?.success == true
                ? new WMAccountResult(true, "验证码已发送，请检查邮箱和垃圾邮件目录。")
                : await FailureAsync("account-code-failed", result?.message?.content ?? "验证码发送失败。").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await FailureAsync("account-code-failed", ex.Message, ex).ConfigureAwait(false);
        }
    }

    public async Task SignOutAsync()
    {
        Global.CurrentUser = new WMLoginChildModel();
        await Global.WriteAccount2LocalAsync(string.Empty, string.Empty).ConfigureAwait(false);
        Changed?.Invoke();
        await LogAsync("account-signed-out", WMDiagnosticLogLevel.Information, "本地账号已退出。").ConfigureAwait(false);
    }

    private async Task<WMAccountResult> UpdatePasswordAsync(
        string email,
        string oldPassword,
        string newPassword,
        string verificationCode,
        bool recovery,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var validation = ValidateEmailAndPassword(email, newPassword);
        if (validation is not null) return new WMAccountResult(false, validation);
        if (recovery && string.IsNullOrWhiteSpace(verificationCode))
            return new WMAccountResult(false, "请输入验证码。");
        if (!recovery && string.IsNullOrWhiteSpace(oldPassword))
            return new WMAccountResult(false, "请输入当前密码。");
        try
        {
            EnsurePrimaryKey();
            var result = await api.UpdateUserInfo(new WMSysUser
            {
                USER_NAME = email.Trim(),
                DISPLAY_NAME = State.DisplayName,
                PASSWORD = recovery ? string.Empty : oldPassword,
                PK_ID = Global.PrimaryKey,
                Code = verificationCode.Trim()
            }, newPassword).ConfigureAwait(false);
            if (result?.success != true)
                return await FailureAsync("account-password-failed", result?.message?.content ?? "密码修改失败。").ConfigureAwait(false);
            if (!recovery)
            {
                await Global.WriteAccount2LocalAsync(email.Trim(), api.GetMD5(newPassword)).ConfigureAwait(false);
                await RefreshAsync(token).ConfigureAwait(false);
            }
            await LogAsync("account-password-completed", WMDiagnosticLogLevel.Information, recovery ? "密码找回成功。" : "密码修改成功。").ConfigureAwait(false);
            return new WMAccountResult(true, recovery ? "密码已重置，请重新登录。" : "密码修改成功。");
        }
        catch (Exception ex)
        {
            return await FailureAsync("account-password-failed", ex.Message, ex).ConfigureAwait(false);
        }
    }

    private void EnsurePrimaryKey()
    {
        if (string.IsNullOrWhiteSpace(Global.PrimaryKey)) Global.PrimaryKey = client.Key();
    }

    private async Task<WMAccountResult> FailureAsync(string eventName, string message, Exception? ex = null)
    {
        await LogAsync(eventName, WMDiagnosticLogLevel.Warning, message, ex).ConfigureAwait(false);
        return new WMAccountResult(false, string.IsNullOrWhiteSpace(message) ? "操作失败，请稍后重试。" : message);
    }

    private Task LogAsync(string eventName, WMDiagnosticLogLevel level, string message, Exception? ex = null) =>
        traces?.RecordLogAsync(new WMDiagnosticLogEvent(
            DateTime.UtcNow, level, "Application.Account", eventName, message,
            ex?.GetType().FullName, ex is null ? null : $"0x{ex.HResult:X8}", StackTrace: ex?.StackTrace))
        ?? Task.CompletedTask;

    private static string? ValidateEmailAndPassword(string email, string password)
    {
        if (!IsEmail(email)) return "邮箱格式不正确。";
        return password.Length < 9 ? "密码至少需要9个字符。" : null;
    }

    public static bool IsEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return new System.Net.Mail.MailAddress(value.Trim()).Address == value.Trim(); }
        catch { return false; }
    }

    private static WMAccountState Project(WMLoginChildModel? user) => new(
        !string.IsNullOrWhiteSpace(user?.ID), user?.ID,
        string.IsNullOrWhiteSpace(user?.DISPLAY_NAME) ? "轻影用户" : user.DISPLAY_NAME,
        user?.USER_NAME, ResolveAvatarUrl(user?.IMG), user?.IsVIP == true, user?.COINS ?? 0, user?.EXPIRE_DATE);

    internal static string? ResolveAvatarUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim();
        if (candidate.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return candidate;
        if (candidate.StartsWith("//", StringComparison.Ordinal)) candidate = "https:" + candidate;

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme is not ("http" or "https")) return null;
            if (absolute.Scheme == "http" && absolute.Host.EndsWith("thankful.top", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(absolute) { Scheme = "https", Port = -1 };
                return builder.Uri.AbsoluteUri;
            }
            return absolute.AbsoluteUri;
        }

        if (candidate.Contains('\\') || candidate.Contains("..", StringComparison.Ordinal)) return null;
        return $"https://cdn.thankful.top/{candidate.TrimStart('/')}";
    }

    private static WMLoginChildModel FromUser(WMSysUser user) => new()
    {
        ID = user.ID ?? string.Empty,
        IMG = string.Empty,
        DISPLAY_NAME = user.DISPLAY_NAME,
        USER_NAME = user.USER_NAME,
        EXPIRE_DATE = user.EXPIRE_DATE,
        COINS = user.COINS
    };
}

public sealed record WMAppSettingsState(
    bool EnhancedExif,
    int MaximumThreads,
    int MaximumAllowedThreads,
    bool PrivacyAccepted);

public interface IWMAppSettingsService
{
    Task<WMAppSettingsState> LoadAsync(CancellationToken token = default);
    Task<WMAppSettingsState> UpdateAsync(bool enhancedExif, int maximumThreads, CancellationToken token = default);
    Task SetPrivacyConsentAsync(bool accepted, CancellationToken token = default);
    Task RevokePrivacyConsentAsync(CancellationToken token = default);
}

public sealed class WMAppSettingsService(
    IWMAccountService accounts,
    IWMWorkspaceTraceStore? traces = null) : IWMAppSettingsService
{
    public async Task<WMAppSettingsState> LoadAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        await GlobalConfig.InitConfig().ConfigureAwait(false);
        return Current();
    }

    public async Task<WMAppSettingsState> UpdateAsync(bool enhancedExif, int maximumThreads, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        GlobalConfig.SECOND_EXIF = enhancedExif;
        GlobalConfig.MAX_THREAD = Math.Clamp(maximumThreads, 1, MaximumAllowedThreads);
        await RecordAsync("settings-updated", "应用设置已更新。").ConfigureAwait(false);
        return Current();
    }

    public async Task RevokePrivacyConsentAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        GlobalConfig.AGREE_PRIVATE = false;
        await accounts.SignOutAsync().ConfigureAwait(false);
        await RecordAsync("privacy-consent-revoked", "本地隐私授权已撤回。").ConfigureAwait(false);
    }

    public async Task SetPrivacyConsentAsync(bool accepted, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        GlobalConfig.AGREE_PRIVATE = accepted;
        await RecordAsync(accepted ? "privacy-consent-accepted" : "privacy-consent-declined",
            accepted ? "本地隐私授权已接受。" : "本地隐私授权未接受。").ConfigureAwait(false);
    }

    private static int MaximumAllowedThreads => Math.Max(1, Environment.ProcessorCount - 1);
    private static WMAppSettingsState Current() => new(
        GlobalConfig.SECOND_EXIF,
        Math.Clamp(GlobalConfig.MAX_THREAD, 1, MaximumAllowedThreads),
        MaximumAllowedThreads,
        GlobalConfig.AGREE_PRIVATE);

    private Task RecordAsync(string eventName, string message) => traces?.RecordLogAsync(
        new WMDiagnosticLogEvent(DateTime.UtcNow, WMDiagnosticLogLevel.Information,
            "Application.Settings", eventName, message)) ?? Task.CompletedTask;
}

public enum WMCacheArea { Temporary, MarketPreviews, LogoLibrary }
public sealed record WMCacheSummary(long TemporaryBytes, long MarketPreviewBytes, long LogoBytes)
{
    public long TotalBytes => TemporaryBytes + MarketPreviewBytes + LogoBytes;
}
public sealed record WMCacheResult(bool Succeeded, string Message, WMCacheSummary Summary);

public interface IWMCacheMaintenanceService
{
    Task<WMCacheSummary> MeasureAsync(CancellationToken token = default);
    Task<WMCacheResult> ClearAsync(WMCacheArea area, CancellationToken token = default);
}

public sealed class WMCacheMaintenanceService(
    IWMWorkspaceSessionStore sessions,
    APIHelper api,
    IWMWorkspaceTraceStore? traces = null) : IWMCacheMaintenanceService
{
    public Task<WMCacheSummary> MeasureAsync(CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var cacheRoot = Path.Combine(Global.AppPath.BasePath, "Cache");
        var previewRoot = Path.Combine(cacheRoot, "template-previews");
        return new WMCacheSummary(
            SizeOf(cacheRoot, previewRoot),
            SizeOf(previewRoot),
            SizeOf(Global.AppPath.LogoesFolder));
    }, token);

    public async Task<WMCacheResult> ClearAsync(WMCacheArea area, CancellationToken token = default)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            switch (area)
            {
                case WMCacheArea.Temporary:
                    await sessions.CleanupExpiredAsync(token).ConfigureAwait(false);
                    DeleteDirectory(Path.Combine(Global.AppPath.BasePath, "Cache", "template-previews"));
                    break;
                case WMCacheArea.MarketPreviews:
                    DeleteDirectory(Global.AppPath.MarketFolder);
                    break;
                case WMCacheArea.LogoLibrary:
                    DeleteDirectory(Global.AppPath.LogoesFolder);
                    await api.DownloadLogoes().ConfigureAwait(false);
                    break;
            }
            var summary = await MeasureAsync(token).ConfigureAwait(false);
            await RecordAsync("cache-cleared", area.ToString()).ConfigureAwait(false);
            return new WMCacheResult(true, "清理完成。", summary);
        }
        catch (Exception ex)
        {
            var summary = await MeasureAsync(CancellationToken.None).ConfigureAwait(false);
            await RecordAsync("cache-clear-failed", ex.GetType().Name).ConfigureAwait(false);
            return new WMCacheResult(false, ex.Message, summary);
        }
    }

    private static long SizeOf(string root, string? excludedRoot = null)
    {
        try
        {
            if (!Directory.Exists(root)) return 0;
            var excluded = string.IsNullOrWhiteSpace(excludedRoot) ? null : Path.GetFullPath(excludedRoot);
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(path =>
            {
                try
                {
                    if (excluded is not null && Path.GetFullPath(path).StartsWith(excluded, StringComparison.Ordinal)) return 0L;
                    return new FileInfo(path).Length;
                }
                catch { return 0L; }
            });
        }
        catch { return 0; }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
    }

    private Task RecordAsync(string eventName, string area) => traces?.RecordLogAsync(
        new WMDiagnosticLogEvent(DateTime.UtcNow, WMDiagnosticLogLevel.Information,
            "Application.Cache", eventName, Properties: new Dictionary<string, string> { ["area"] = area }))
        ?? Task.CompletedTask;
}

public enum WMResourceKind { Logo, Font }
public sealed record WMResourceItem(
    string Id,
    string Name,
    WMResourceKind Kind,
    bool Installed,
    string? PreviewUrl = null);
public sealed record WMResourceResult(bool Succeeded, string Message);

public interface IWMResourceLibraryService
{
    Task<IReadOnlyList<WMResourceItem>> ListAsync(WMResourceKind kind, CancellationToken token = default);
    Task<WMResourceResult> ImportAsync(WMResourceKind kind, string fileName, Stream content, CancellationToken token = default);
    Task<WMResourceResult> DownloadFontAsync(string fontName, CancellationToken token = default);
    Task<WMResourceResult> DeleteAsync(WMResourceKind kind, string id, CancellationToken token = default);
}

public sealed class WMResourceLibraryService(APIHelper api) : IWMResourceLibraryService
{
    public async Task<IReadOnlyList<WMResourceItem>> ListAsync(WMResourceKind kind, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (kind == WMResourceKind.Logo)
        {
            Directory.CreateDirectory(Global.AppPath.LogoesFolder);
            return Directory.EnumerateFiles(Global.AppPath.LogoesFolder)
                .Where(IsImage)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => new WMResourceItem(
                    Path.GetFileName(path), Path.GetFileName(path), kind, true, ToDataUrl(path)))
                .ToArray();
        }

        Directory.CreateDirectory(Global.AppPath.FontFolder);
        var fonts = await Task.Run(() => ReadInstalledFonts(token), token).ConfigureAwait(false);

        try
        {
            var cloud = await Connections.HttpGetAsync<List<WMCloudFont>>(
                APIHelper.HOST + "/api/CloudSync/GetFontsList", Encoding.UTF8).ConfigureAwait(false);
            if (cloud?.success == true)
            {
                foreach (var item in cloud.data.Where(item => !string.IsNullOrWhiteSpace(item.NAME)))
                {
                    token.ThrowIfCancellationRequested();
                    var fileName = Path.GetFileName(item.NAME);
                    if (string.IsNullOrWhiteSpace(fileName) || fonts.ContainsKey(fileName)) continue;
                    fonts[fileName] = new WMResourceItem(fileName, fileName, kind, false);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Local fonts remain useful offline; an unavailable marketplace must not hide them.
        }

        return fonts.Values
            .OrderByDescending(item => item.Installed)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<WMResourceResult> ImportAsync(
        WMResourceKind kind,
        string fileName,
        Stream content,
        CancellationToken token = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (kind == WMResourceKind.Logo && extension is not ".png" and not ".jpg" and not ".jpeg")
            return new WMResourceResult(false, "图标仅支持PNG和JPEG。");
        if (kind == WMResourceKind.Font && extension is not ".ttf" and not ".otf")
            return new WMResourceResult(false, "字体仅支持TTF和OTF。");
        var directory = kind == WMResourceKind.Logo ? Global.AppPath.LogoesFolder : Global.AppPath.FontFolder;
        Directory.CreateDirectory(directory);
        var safeName = kind == WMResourceKind.Logo
            ? $"{Guid.NewGuid():N}{extension}"
            : Path.GetFileName(fileName);
        var target = Path.Combine(directory, safeName);
        var temporary = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
                await content.CopyToAsync(output, token).ConfigureAwait(false);
            File.Move(temporary, target, true);
            return new WMResourceResult(true, "导入完成。");
        }
        catch (Exception ex) { return new WMResourceResult(false, ex.Message); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    public async Task<WMResourceResult> DownloadFontAsync(string fontName, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            await api.DownloadFonts([Path.GetFileName(fontName)]).ConfigureAwait(false);
            return File.Exists(Path.Combine(Global.AppPath.FontFolder, Path.GetFileName(fontName)))
                ? new WMResourceResult(true, "字体下载完成。")
                : new WMResourceResult(false, "字体下载失败。");
        }
        catch (Exception ex) { return new WMResourceResult(false, ex.Message); }
    }

    public Task<WMResourceResult> DeleteAsync(WMResourceKind kind, string id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var directory = kind == WMResourceKind.Logo ? Global.AppPath.LogoesFolder : Global.AppPath.FontFolder;
            var root = Path.GetFullPath(directory);
            var path = Path.GetFullPath(Path.Combine(root, Path.GetFileName(id)));
            if (!path.StartsWith(root, StringComparison.Ordinal)) return Task.FromResult(new WMResourceResult(false, "资源路径无效。"));
            if (File.Exists(path)) File.Delete(path);
            return Task.FromResult(new WMResourceResult(true, "资源已删除。"));
        }
        catch (Exception ex) { return Task.FromResult(new WMResourceResult(false, ex.Message)); }
    }

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
    private static bool IsFont(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf";

    private static Dictionary<string, WMResourceItem> ReadInstalledFonts(CancellationToken token)
    {
        var result = new Dictionary<string, WMResourceItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(Global.AppPath.FontFolder).Where(IsFont))
        {
            token.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            result[fileName] = new WMResourceItem(
                fileName,
                fileName,
                WMResourceKind.Font,
                true,
                CreateFontPreviewDataUrl(path));
        }
        return result;
    }

    private static string? CreateFontPreviewDataUrl(string path)
    {
        try
        {
            using var typeface = SKTypeface.FromFile(path);
            if (typeface is null) return null;
            using var bitmap = new SKBitmap(520, 116, true);
            using var canvas = new SKCanvas(bitmap);
            using var samplePaint = new SKPaint
            {
                Color = SKColor.Parse("#172033"),
                IsAntialias = true,
                TextSize = 30,
                Typeface = typeface
            };
            using var captionPaint = new SKPaint
            {
                Color = SKColor.Parse("#687386"),
                IsAntialias = true,
                TextSize = 18,
                Typeface = typeface
            };
            canvas.Clear(SKColor.Parse("#F6F7F9"));
            canvas.DrawText("Light & Shadow  0123", 18, 48, samplePaint);
            canvas.DrawText(typeface.FamilyName ?? Path.GetFileNameWithoutExtension(path), 18, 86, captionPaint);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
            return encoded is null ? null : $"data:image/png;base64,{Convert.ToBase64String(encoded.ToArray())}";
        }
        catch { return null; }
    }

    private static string? ToDataUrl(string path)
    {
        try
        {
            var mime = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        }
        catch { return null; }
    }
}

public sealed record WMUpdateState(
    string CurrentVersion,
    bool IsChecking = false,
    bool UpdateAvailable = false,
    string? AvailableVersion = null,
    string? Message = null,
    bool HasError = false);

public interface IWMAppUpdateService
{
    WMUpdateState State { get; }
    Task<WMUpdateState> CheckAsync(CancellationToken token = default);
    Task<WMResourceResult> StartUpdateAsync(CancellationToken token = default);
}

public sealed class WMAppUpdateService(IClientInstance client) : IWMAppUpdateService
{
    public WMUpdateState State { get; private set; } = new(client.GetVersion().ToString());

    public async Task<WMUpdateState> CheckAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        State = State with { IsChecking = true, Message = "正在检查更新…", HasError = false };
        try
        {
            var available = await client.CheckUpdate().ConfigureAwait(false);
            State = State with
            {
                IsChecking = false,
                UpdateAvailable = available,
                AvailableVersion = available ? client.UpdateVersion : null,
                Message = available ? $"发现新版本 {client.UpdateVersion}" : "已经是最新版本。",
                HasError = false
            };
        }
        catch (Exception ex) { State = State with { IsChecking = false, Message = ex.Message, HasError = true }; }
        return State;
    }

    public async Task<WMResourceResult> StartUpdateAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            await client.Update((_, _) => { }).ConfigureAwait(false);
            return new WMResourceResult(true, "已打开更新页面。");
        }
        catch (Exception ex) { return new WMResourceResult(false, ex.Message); }
    }
}

public interface IWMExternalActionService
{
    Task OpenUrlAsync(string url);
    Task CopyTextAsync(string text);
}

public interface IWMHostNavigationBridge
{
    event Action<string>? NavigationRequested;
    void Navigate(string route);
}

public sealed class WMHostNavigationBridge : IWMHostNavigationBridge
{
    public event Action<string>? NavigationRequested;

    public void Navigate(string route)
    {
        var safeRoute = WMReturnUrl.Normalize(route, "/create");
        NavigationRequested?.Invoke(safeRoute);
    }
}

public sealed class WMExternalActionService(IClientInstance client) : IWMExternalActionService
{
    public Task OpenUrlAsync(string url) => client.OpenExternalUrlAsync(url);
    public Task CopyTextAsync(string text) => client.SetTextAsync(text);
}

public sealed record WMMembershipPlan(string Id, string Name, decimal Price, string Description, bool Recommended = false);

public interface IWMMembershipService
{
    IReadOnlyList<WMMembershipPlan> Plans { get; }
    Task<WMMembershipResult> PurchaseAsync(string planId, CancellationToken token = default);
    Task<WMMembershipResult> RefreshAsync(string orderId, CancellationToken token = default);
    Task<WMMembershipResult?> ReconcilePendingAsync(CancellationToken token = default);
}

public sealed class WMMembershipService(
    IWMMembershipPaymentGateway payments,
    IWMAlipayAppLauncher alipay,
    IWMPendingMembershipStore pendingOrders,
    IWMMembershipPaymentClock clock,
    IWMAccountService accounts,
    IWMExternalActionService external,
    IWMWorkspaceTraceStore? traces = null) : IWMMembershipService
{
    private static readonly TimeSpan[] InteractiveQueryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(4)
    ];
    private static readonly TimeSpan[] AccountRefreshDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2)
    ];
    private static readonly WMMembershipPlan[] RegularPlans =
    [
        new("year", "年度会员", 28, "适合长期批量处理", true),
        new("quarter", "季度会员", 18, "适合阶段性创作"),
        new("month", "月度会员", 8, "轻量体验高级能力")
    ];
    private readonly SemaphoreSlim purchaseGate = new(1, 1);

    public IReadOnlyList<WMMembershipPlan> Plans =>
        string.Equals(accounts.State.UserName, "xlz", StringComparison.OrdinalIgnoreCase)
            ? [.. RegularPlans, new WMMembershipPlan("test", "测试套餐", 0.01m, "支付成功后增加 1 分钟会员")]
            : RegularPlans;

    public async Task<WMMembershipResult> PurchaseAsync(string planId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var plan = Plans.FirstOrDefault(item => item.Id == planId);
        if (plan is null) return Failed("会员套餐不存在。");
        if (!accounts.State.IsAuthenticated) return Failed("请先登录后再开通会员。");
        if (!await purchaseGate.WaitAsync(0, token).ConfigureAwait(false))
            return Pending("支付流程正在进行，请勿重复操作。");
        try
        {
            if (Global.DeviceType == DeviceType.Andorid)
                return await PurchaseAndroidAsync(plan, token).ConfigureAwait(false);

            var order = await payments.CreateDesktopOrderAsync(
                plan.Price, plan.Name, accounts.State.UserId ?? string.Empty, token).ConfigureAwait(false);
            if (order?.success != true || order.data is null || string.IsNullOrWhiteSpace(order.data.PayUrl))
                return Failed(order?.message?.content ?? "暂时无法创建支付订单。");
            await external.OpenUrlAsync(order.data.PayUrl).ConfigureAwait(false);
            return new WMMembershipResult(
                WMMembershipPaymentState.Pending,
                "支付页面已打开。",
                order.data.OutTradeNo,
                order.data.PayUrl);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await LogAsync("membership-purchase-failed", WMDiagnosticLogLevel.Error, ex.Message, exception: ex)
                .ConfigureAwait(false);
            return Failed(ex.Message);
        }
        finally
        {
            purchaseGate.Release();
        }
    }

    public async Task<WMMembershipResult> RefreshAsync(string orderId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(orderId)) return Failed("没有待查询订单。");
        await purchaseGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Global.DeviceType != DeviceType.Andorid)
                return await ReconcileDesktopAsync(orderId, token).ConfigureAwait(false);

            var userId = accounts.State.UserId;
            if (string.IsNullOrWhiteSpace(userId)) return Failed("请重新登录后查询支付状态。", orderId);
            var pending = await pendingOrders.GetAsync(userId, token).ConfigureAwait(false);
            if (pending is null || !string.Equals(pending.OutTradeNo, orderId, StringComparison.Ordinal))
            {
                pending = new WMPendingMembershipOrder(userId, orderId, "unknown", clock.UtcNow);
                await pendingOrders.SaveAsync(pending, token).ConfigureAwait(false);
            }
            return await ReconcileAndroidOrderAsync(pending, false, false, token).ConfigureAwait(false);
        }
        finally
        {
            purchaseGate.Release();
        }
    }

    public async Task<WMMembershipResult?> ReconcilePendingAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (Global.DeviceType != DeviceType.Andorid || !accounts.State.IsAuthenticated) return null;
        await purchaseGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var userId = accounts.State.UserId;
            if (string.IsNullOrWhiteSpace(userId)) return null;
            var pending = await pendingOrders.GetAsync(userId, token).ConfigureAwait(false);
            if (pending is null) return null;
            if (!string.Equals(pending.UserId, userId, StringComparison.Ordinal))
            {
                await LogAsync(
                    "membership-pending-user-mismatch",
                    WMDiagnosticLogLevel.Warning,
                    "忽略了不属于当前账号的待确认订单。",
                    pending.OutTradeNo).ConfigureAwait(false);
                return null;
            }
            return await ReconcileAndroidOrderAsync(pending, false, false, token).ConfigureAwait(false);
        }
        finally
        {
            purchaseGate.Release();
        }
    }

    private async Task<WMMembershipResult> PurchaseAndroidAsync(
        WMMembershipPlan plan,
        CancellationToken token)
    {
        var userId = accounts.State.UserId;
        if (string.IsNullOrWhiteSpace(userId)) return Failed("请先登录后再开通会员。");

        var previous = await pendingOrders.GetAsync(userId, token).ConfigureAwait(false);
        if (previous is not null)
        {
            var recovered = await ReconcileAndroidOrderAsync(previous, false, false, token).ConfigureAwait(false);
            if (recovered.State is WMMembershipPaymentState.Paid or WMMembershipPaymentState.Pending)
                return recovered.State == WMMembershipPaymentState.Pending
                    ? recovered with { Message = "上一笔订单仍在确认中，请勿重复付款。" }
                    : recovered;
        }

        var order = await payments.CreateAndroidOrderAsync(plan.Price, plan.Name, userId, token)
            .ConfigureAwait(false);
        if (order?.success != true || string.IsNullOrWhiteSpace(order.data))
            return Failed(order?.message?.content ?? "暂时无法创建支付宝订单。");
        if (!WMAlipayOrderInfoParser.TryGetOutTradeNo(order.data, out var outTradeNo))
        {
            await LogAsync(
                "membership-order-parse-failed",
                WMDiagnosticLogLevel.Error,
                "服务端支付宝订单缺少商户订单号。").ConfigureAwait(false);
            return Failed("支付宝订单信息不完整，未发起支付。");
        }

        var pending = new WMPendingMembershipOrder(userId, outTradeNo, plan.Id, clock.UtcNow);
        await pendingOrders.SaveAsync(pending, token).ConfigureAwait(false);
        await LogAsync(
            "membership-order-created",
            WMDiagnosticLogLevel.Information,
            "安卓会员订单已创建并保存。",
            outTradeNo).ConfigureAwait(false);

        WMAlipayAppLaunchResult launchResult;
        try
        {
            launchResult = await alipay.LaunchAsync(order.data, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await LogAsync(
                "membership-alipay-launch-failed",
                WMDiagnosticLogLevel.Error,
                ex.Message,
                outTradeNo,
                exception: ex).ConfigureAwait(false);
            var recovered = await ReconcileAndroidOrderAsync(pending, false, false, token).ConfigureAwait(false);
            return recovered.State == WMMembershipPaymentState.Paid
                ? recovered
                : Pending("支付宝调用异常，支付状态待确认，请勿重复付款。", outTradeNo);
        }

        await LogAsync(
            "membership-alipay-returned",
            WMDiagnosticLogLevel.Information,
            "支付宝 App 已返回。",
            outTradeNo,
            launchResult.ResultStatus).ConfigureAwait(false);

        var cancelled = string.Equals(launchResult.ResultStatus, "6001", StringComparison.Ordinal);
        return await ReconcileAndroidOrderAsync(pending, !cancelled, cancelled, token).ConfigureAwait(false);
    }

    private async Task<WMMembershipResult> ReconcileAndroidOrderAsync(
        WMPendingMembershipOrder pending,
        bool interactive,
        bool clientCancelled,
        CancellationToken token)
    {
        var delays = interactive ? InteractiveQueryDelays : [TimeSpan.Zero];
        var queryReachedServer = false;
        string? lastMessage = null;
        foreach (var delay in delays)
        {
            if (delay > TimeSpan.Zero) await clock.DelayAsync(delay, token).ConfigureAwait(false);
            API<DesktopPayStatus>? result;
            try
            {
                result = await payments.QueryAsync(pending.OutTradeNo, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastMessage = ex.Message;
                await LogAsync(
                    "membership-query-failed",
                    WMDiagnosticLogLevel.Warning,
                    ex.Message,
                    pending.OutTradeNo,
                    exception: ex).ConfigureAwait(false);
                continue;
            }

            if (result?.success != true || result.data is null)
            {
                lastMessage = result?.message?.content;
                await LogAsync(
                    "membership-query-failed",
                    WMDiagnosticLogLevel.Warning,
                    lastMessage ?? "支付状态查询失败。",
                    pending.OutTradeNo).ConfigureAwait(false);
                continue;
            }

            queryReachedServer = true;
            var status = result.data.Status?.Trim() ?? string.Empty;
            lastMessage = result.data.Message;
            await LogAsync(
                "membership-query-completed",
                WMDiagnosticLogLevel.Information,
                string.IsNullOrWhiteSpace(status) ? "UNKNOWN" : status,
                pending.OutTradeNo,
                status).ConfigureAwait(false);

            if (string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                if (!await RefreshAccountUntilPaidAsync(result.data.ExpireDate, token).ConfigureAwait(false))
                    return Pending("支付已确认，会员状态同步中，请稍后刷新。", pending.OutTradeNo);

                await pendingOrders.DeleteAsync(pending.UserId, pending.OutTradeNo, token).ConfigureAwait(false);
                await LogAsync(
                    "membership-entitlement-confirmed",
                    WMDiagnosticLogLevel.Information,
                    "会员状态已刷新。",
                    pending.OutTradeNo).ConfigureAwait(false);
                return Paid("支付成功，会员已开通。");
            }

            if (string.Equals(status, "CLOSED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                await pendingOrders.DeleteAsync(pending.UserId, pending.OutTradeNo, token).ConfigureAwait(false);
                return Failed(result.data.Message ?? "订单未支付成功。", pending.OutTradeNo);
            }

            if (clientCancelled)
            {
                await pendingOrders.DeleteAsync(pending.UserId, pending.OutTradeNo, token).ConfigureAwait(false);
                return Cancelled("已取消支付。", pending.OutTradeNo);
            }
        }

        var message = queryReachedServer
            ? "支付结果确认中，请勿重复付款。"
            : string.IsNullOrWhiteSpace(lastMessage)
                ? "暂时无法查询支付结果，请勿重复付款，稍后刷新。"
                : $"支付结果暂时无法确认：{lastMessage}。请勿重复付款。";
        return Pending(message, pending.OutTradeNo);
    }

    private async Task<WMMembershipResult> ReconcileDesktopAsync(
        string orderId,
        CancellationToken token)
    {
        API<DesktopPayStatus>? result;
        try
        {
            result = await payments.QueryAsync(orderId, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Pending(ex.Message, orderId);
        }
        if (result?.success != true || result.data is null)
            return Pending(result?.message?.content ?? "支付状态查询失败。", orderId);
        var paid = string.Equals(result.data.Status, "PAID", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(result.data.Status, "TRADE_SUCCESS", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(result.data.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
                   || result.data.ExpireDate > DateTime.Now;
        if (paid)
        {
            await accounts.RefreshAsync(token).ConfigureAwait(false);
            return Paid(result.data.Message ?? "支付成功。");
        }
        if (string.Equals(result.data.Status, "CLOSED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result.data.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
            return Failed(result.data.Message ?? "支付未完成。", orderId);
        return Pending(result.data.Message ?? "等待支付。", orderId);
    }

    private async Task<bool> RefreshAccountUntilPaidAsync(
        DateTime? expectedExpireDate,
        CancellationToken token)
    {
        foreach (var delay in AccountRefreshDelays)
        {
            if (delay > TimeSpan.Zero) await clock.DelayAsync(delay, token).ConfigureAwait(false);
            await accounts.RefreshAsync(token).ConfigureAwait(false);
            if (AccountReflectsPaidOrder(accounts.State, expectedExpireDate)) return true;
        }
        return false;
    }

    private static bool AccountReflectsPaidOrder(WMAccountState state, DateTime? expectedExpireDate)
    {
        if (!state.IsVip) return false;
        if (expectedExpireDate is null) return true;
        return state.ExpiresAt is not null && state.ExpiresAt.Value >= expectedExpireDate.Value.AddSeconds(-2);
    }

    private async Task LogAsync(
        string eventName,
        WMDiagnosticLogLevel level,
        string message,
        string? orderId = null,
        string? resultStatus = null,
        Exception? exception = null)
    {
        if (traces is null) return;
        if (!string.IsNullOrWhiteSpace(orderId))
            message = message.Replace(orderId, OrderTail(orderId), StringComparison.Ordinal);
        var properties = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(orderId)) properties["orderTail"] = OrderTail(orderId);
        if (!string.IsNullOrWhiteSpace(resultStatus)) properties["resultStatus"] = resultStatus;
        try
        {
            await traces.RecordLogAsync(new WMDiagnosticLogEvent(
                DateTime.UtcNow,
                level,
                "Application.Membership",
                eventName,
                message,
                exception?.GetType().FullName,
                exception is null ? null : $"0x{exception.HResult:X8}",
                Properties: properties.Count == 0 ? null : properties,
                StackTrace: exception?.StackTrace)).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string OrderTail(string orderId) =>
        orderId.Length <= 8 ? orderId : orderId[^8..];

    private static WMMembershipResult Paid(string message) =>
        new(WMMembershipPaymentState.Paid, message);
    private static WMMembershipResult Pending(string message, string? orderId = null) =>
        new(WMMembershipPaymentState.Pending, message, orderId);
    private static WMMembershipResult Cancelled(string message, string? orderId = null) =>
        new(WMMembershipPaymentState.Cancelled, message, orderId);
    private static WMMembershipResult Failed(string message, string? orderId = null) =>
        new(WMMembershipPaymentState.Failed, message, orderId);
}

public interface IWMAdminDashboardService
{
    bool IsAuthorized { get; }
    Task<DashboardOverview?> LoadAsync(DateTime startDate, DateTime endDate, CancellationToken token = default);
    string? LastError { get; }
}

public sealed class WMAdminDashboardService(APIHelper api, IWMAccountService accounts) : IWMAdminDashboardService
{
    public bool IsAuthorized => AdminAccessPolicy.IsAdmin(Global.CurrentUser) && accounts.State.IsAuthenticated;
    public string? LastError { get; private set; }

    public async Task<DashboardOverview?> LoadAsync(DateTime startDate, DateTime endDate, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        LastError = null;
        if (!IsAuthorized) { LastError = "当前账号没有管理员权限。"; return null; }
        var result = await api.GetDashboardOverviewAsync(startDate, endDate).ConfigureAwait(false);
        if (result?.success == true && result.data is not null) return result.data;
        LastError = result?.message?.content ?? "看板数据加载失败。";
        return null;
    }
}

public static class WMReturnUrl
{
    public static string Normalize(string? value, string fallback = "/profile")
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('/') || value.StartsWith("//")) return fallback;
        if (!Uri.TryCreate(value, UriKind.Relative, out _)) return fallback;
        if (value.Contains('\\') || value.Contains("..", StringComparison.Ordinal)) return fallback;
        return value;
    }
}
