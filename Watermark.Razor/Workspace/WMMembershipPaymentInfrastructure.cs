#nullable enable

using System.Text.Json;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public enum WMMembershipPaymentState
{
    Paid,
    Pending,
    Cancelled,
    Failed
}

public sealed record WMMembershipResult(
    WMMembershipPaymentState State,
    string Message,
    string? OrderId = null,
    string? PaymentUrl = null)
{
    public bool Succeeded => State == WMMembershipPaymentState.Paid;
}

public sealed record WMPendingMembershipOrder(
    string UserId,
    string OutTradeNo,
    string PlanId,
    DateTime CreatedAtUtc);

public interface IWMPendingMembershipStore
{
    Task<WMPendingMembershipOrder?> GetAsync(string userId, CancellationToken token = default);
    Task SaveAsync(WMPendingMembershipOrder order, CancellationToken token = default);
    Task DeleteAsync(string userId, string outTradeNo, CancellationToken token = default);
}

public sealed class WMPendingMembershipStore : IWMPendingMembershipStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;

    public WMPendingMembershipStore()
        : this(Path.Combine(Global.AppPath.BasePath, "sys", "pending-membership.json"))
    {
    }

    public WMPendingMembershipStore(string path)
    {
        this.path = path;
    }

    public async Task<WMPendingMembershipOrder?> GetAsync(
        string userId,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await ReadAsync(token).ConfigureAwait(false);
            return state.Orders.FirstOrDefault(order =>
                string.Equals(order.UserId, userId, StringComparison.Ordinal));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(WMPendingMembershipOrder order, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (string.IsNullOrWhiteSpace(order.UserId) || string.IsNullOrWhiteSpace(order.OutTradeNo))
            throw new ArgumentException("待确认会员订单缺少用户或订单号。", nameof(order));

        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await ReadAsync(token).ConfigureAwait(false);
            state.Orders.RemoveAll(item => string.Equals(item.UserId, order.UserId, StringComparison.Ordinal));
            state.Orders.Add(order);
            await WriteAsync(state, token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(
        string userId,
        string outTradeNo,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(outTradeNo)) return;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await ReadAsync(token).ConfigureAwait(false);
            var removed = state.Orders.RemoveAll(order =>
                string.Equals(order.UserId, userId, StringComparison.Ordinal)
                && string.Equals(order.OutTradeNo, outTradeNo, StringComparison.Ordinal)) > 0;
            if (!removed) return;
            if (state.Orders.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            await WriteAsync(state, token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PendingMembershipState> ReadAsync(CancellationToken token)
    {
        try
        {
            if (!File.Exists(path)) return new PendingMembershipState();
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<PendingMembershipState>(stream, JsonOptions, token)
                       .ConfigureAwait(false)
                   ?? new PendingMembershipState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new PendingMembershipState();
        }
    }

    private async Task WriteAsync(PendingMembershipState state, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("待确认订单存储路径无效。");
        Directory.CreateDirectory(directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }

    private sealed class PendingMembershipState
    {
        public List<WMPendingMembershipOrder> Orders { get; set; } = [];
    }
}

public static class WMAlipayOrderInfoParser
{
    public static bool TryGetOutTradeNo(string? orderInfo, out string outTradeNo)
    {
        outTradeNo = string.Empty;
        if (string.IsNullOrWhiteSpace(orderInfo)) return false;
        try
        {
            foreach (var part in orderInfo.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0) continue;
                var key = Uri.UnescapeDataString(part[..separator]);
                if (!string.Equals(key, "biz_content", StringComparison.OrdinalIgnoreCase)) continue;
                var encoded = part[(separator + 1)..].Replace("+", " ", StringComparison.Ordinal);
                var json = Uri.UnescapeDataString(encoded);
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("out_trade_no", out var property)) return false;
                outTradeNo = property.GetString()?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(outTradeNo);
            }
        }
        catch (Exception ex) when (ex is UriFormatException or JsonException)
        {
        }
        return false;
    }
}

public interface IWMMembershipPaymentGateway
{
    Task<API<string>> CreateAndroidOrderAsync(
        decimal cost,
        string planName,
        string userId,
        CancellationToken token = default);
    Task<API<DesktopPayOrder>> CreateDesktopOrderAsync(
        decimal cost,
        string planName,
        string userId,
        CancellationToken token = default);
    Task<API<DesktopPayStatus>> QueryAsync(string outTradeNo, CancellationToken token = default);
}

public sealed class WMMembershipPaymentGateway(APIHelper api) : IWMMembershipPaymentGateway
{
    public async Task<API<string>> CreateAndroidOrderAsync(
        decimal cost,
        string planName,
        string userId,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var result = await api.GetPayToken(cost, planName, userId).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<API<DesktopPayOrder>> CreateDesktopOrderAsync(
        decimal cost,
        string planName,
        string userId,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var result = await api.CreateDesktopPay(cost, planName, userId).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<API<DesktopPayStatus>> QueryAsync(
        string outTradeNo,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var result = await api.QueryPay(outTradeNo).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return result;
    }
}

public interface IWMAlipayAppLauncher
{
    Task<WMAlipayAppLaunchResult> LaunchAsync(
        string orderInfo,
        CancellationToken token = default);
}

public sealed class WMClientAlipayAppLauncher(IClientInstance client) : IWMAlipayAppLauncher
{
    public Task<WMAlipayAppLaunchResult> LaunchAsync(
        string orderInfo,
        CancellationToken token = default) =>
        client.LaunchAlipayAppAsync(orderInfo, token);
}

public interface IWMMembershipPaymentClock
{
    DateTime UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken token);
}

public sealed class WMSystemMembershipPaymentClock : IWMMembershipPaymentClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public Task DelayAsync(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token);
}
