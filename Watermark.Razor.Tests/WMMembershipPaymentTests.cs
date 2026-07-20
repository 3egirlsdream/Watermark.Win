using System.Text.Json;
using Watermark.Razor.Workspace;
using Watermark.Shared.Enums;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class WMMembershipPaymentCollection
{
    public const string CollectionName = "Membership payment";
}

[Collection(WMMembershipPaymentCollection.CollectionName)]
public sealed class WMMembershipPaymentTests : IDisposable
{
    private const string UserId = "user-android";
    private const string OrderId = "202607161234567890";
    private readonly DeviceType originalDeviceType = Global.DeviceType;

    public WMMembershipPaymentTests()
    {
        Global.DeviceType = DeviceType.Andorid;
    }

    [Fact]
    public void OrderParser_ReadsUrlEncodedBizContent_AndRejectsDamagedOrders()
    {
        Assert.True(WMAlipayOrderInfoParser.TryGetOutTradeNo(OrderInfo(OrderId), out var parsed));
        Assert.Equal(OrderId, parsed);

        Assert.False(WMAlipayOrderInfoParser.TryGetOutTradeNo("app_id=1&biz_content=%7Bbroken", out _));
        Assert.False(WMAlipayOrderInfoParser.TryGetOutTradeNo(
            $"app_id=1&biz_content={Uri.EscapeDataString("{\"subject\":\"会员\"}")}", out _));
    }

    [Fact]
    public async Task Purchase_PersistsPendingOrderBeforeLaunchingAlipay()
    {
        var store = new MemoryPendingStore();
        var launcher = new FakeLauncher(async (_, token) =>
        {
            Assert.NotNull(await store.GetAsync(UserId, token));
            return new WMAlipayAppLaunchResult("9000", "支付成功");
        });
        var accounts = PaidAccountAfterRefresh();
        var gateway = Gateway(
            create: SuccessfulOrder(OrderInfo(OrderId)),
            query: _ => Task.FromResult(PaidQuery()));
        var service = Service(gateway, launcher, store, accounts);

        var result = await service.PurchaseAsync("year");

        Assert.Equal(WMMembershipPaymentState.Paid, result.State);
        Assert.Equal(1, launcher.CallCount);
        Assert.Equal(UserId, gateway.CreatedForUserId);
        Assert.Null(await store.GetAsync(UserId));
    }

    [Fact]
    public async Task Purchase_DamagedOrderDoesNotLaunchAlipayOrPersistPendingOrder()
    {
        var store = new MemoryPendingStore();
        var launcher = new FakeLauncher();
        var gateway = Gateway(create: SuccessfulOrder("app_id=1&sign=secret"));
        var service = Service(gateway, launcher, store, AuthenticatedAccount());

        var result = await service.PurchaseAsync("year");

        Assert.Equal(WMMembershipPaymentState.Failed, result.State);
        Assert.Equal(0, launcher.CallCount);
        Assert.Null(await store.GetAsync(UserId));
    }

    [Fact]
    public async Task Purchase_9000AndPending_PollsTwentySecondsAndKeepsOrder()
    {
        var store = new MemoryPendingStore();
        var clock = new FakeClock();
        var gateway = Gateway(
            create: SuccessfulOrder(OrderInfo(OrderId)),
            query: _ => Task.FromResult(PendingQuery()));
        var service = Service(
            gateway,
            new FakeLauncher(result: new WMAlipayAppLaunchResult("9000", "支付成功")),
            store,
            AuthenticatedAccount(),
            clock);

        var result = await service.PurchaseAsync("year");

        Assert.Equal(WMMembershipPaymentState.Pending, result.State);
        Assert.Equal(OrderId, result.OrderId);
        Assert.Equal(7, gateway.QueryCount);
        Assert.Equal(TimeSpan.FromSeconds(20), clock.TotalDelay);
        Assert.Equal(OrderId, (await store.GetAsync(UserId))?.OutTradeNo);
    }

    [Fact]
    public async Task Purchase_9000AndNetworkFailure_KeepsOrderWithoutReportingFailure()
    {
        var store = new MemoryPendingStore();
        var gateway = Gateway(
            create: SuccessfulOrder(OrderInfo(OrderId)),
            query: _ => throw new HttpRequestException("offline"));
        var service = Service(
            gateway,
            new FakeLauncher(result: new WMAlipayAppLaunchResult("9000", string.Empty)),
            store,
            AuthenticatedAccount());

        var result = await service.PurchaseAsync("year");

        Assert.Equal(WMMembershipPaymentState.Pending, result.State);
        Assert.Equal(OrderId, (await store.GetAsync(UserId))?.OutTradeNo);
    }

    [Fact]
    public async Task Purchase_6001ButServerPaid_StillOpensMembership()
    {
        var store = new MemoryPendingStore();
        var gateway = Gateway(
            create: SuccessfulOrder(OrderInfo(OrderId)),
            query: _ => Task.FromResult(PaidQuery()));
        var service = Service(
            gateway,
            new FakeLauncher(result: new WMAlipayAppLaunchResult("6001", "用户取消")),
            store,
            PaidAccountAfterRefresh());

        var result = await service.PurchaseAsync("year");

        Assert.Equal(WMMembershipPaymentState.Paid, result.State);
        Assert.Equal(1, gateway.QueryCount);
        Assert.Null(await store.GetAsync(UserId));
    }

    [Fact]
    public async Task Purchase_6001AndServerPending_ReturnsCancelledAndClearsOrder()
    {
        var store = new MemoryPendingStore();
        var gateway = Gateway(
            create: SuccessfulOrder(OrderInfo(OrderId)),
            query: _ => Task.FromResult(PendingQuery()));
        var service = Service(
            gateway,
            new FakeLauncher(result: new WMAlipayAppLaunchResult("6001", "用户取消")),
            store,
            AuthenticatedAccount());

        var result = await service.PurchaseAsync("year");

        Assert.Equal(WMMembershipPaymentState.Cancelled, result.State);
        Assert.Equal(1, gateway.QueryCount);
        Assert.Null(await store.GetAsync(UserId));
    }

    [Fact]
    public async Task ReconcilePending_AfterRestartRefreshesMembershipAndClearsOrder()
    {
        var store = new MemoryPendingStore();
        await store.SaveAsync(new WMPendingMembershipOrder(UserId, OrderId, "year", DateTime.UtcNow));
        var accounts = PaidAccountAfterRefresh();
        var gateway = Gateway(query: _ => Task.FromResult(PaidQuery()));
        var service = Service(gateway, new FakeLauncher(), store, accounts);

        var result = await service.ReconcilePendingAsync();

        Assert.NotNull(result);
        Assert.Equal(WMMembershipPaymentState.Paid, result!.State);
        Assert.True(accounts.RefreshCount > 0);
        Assert.Null(await store.GetAsync(UserId));
    }

    [Fact]
    public async Task Purchase_PreviousClosedOrderIsClearedAndANewOrderCanBeCreated()
    {
        const string previousOrderId = "previous-closed-order";
        var store = new MemoryPendingStore();
        await store.SaveAsync(new WMPendingMembershipOrder(
            UserId, previousOrderId, "month", DateTime.UtcNow.AddMinutes(-5)));
        var gateway = Gateway(
            create: SuccessfulOrder(OrderInfo(OrderId)),
            query: orderId => Task.FromResult(
                orderId == previousOrderId ? Query("CLOSED", null) : PaidQuery()));
        var service = Service(
            gateway,
            new FakeLauncher(result: new WMAlipayAppLaunchResult("9000", string.Empty)),
            store,
            PaidAccountAfterRefresh());

        var result = await service.PurchaseAsync("year");

        Assert.Equal(WMMembershipPaymentState.Paid, result.State);
        Assert.Equal(1, gateway.CreateCount);
        Assert.Equal(2, gateway.QueryCount);
        Assert.Null(await store.GetAsync(UserId));
    }

    [Fact]
    public async Task Purchase_RepeatedClickCreatesOnlyOneOrder()
    {
        var store = new MemoryPendingStore();
        var launchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLaunch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new FakeLauncher(async (_, token) =>
        {
            launchStarted.TrySetResult();
            await releaseLaunch.Task.WaitAsync(token);
            return new WMAlipayAppLaunchResult("9000", string.Empty);
        });
        var gateway = Gateway(
            create: SuccessfulOrder(OrderInfo(OrderId)),
            query: _ => Task.FromResult(PaidQuery()));
        var service = Service(gateway, launcher, store, PaidAccountAfterRefresh());

        var first = service.PurchaseAsync("year");
        await launchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = await service.PurchaseAsync("year");
        releaseLaunch.TrySetResult();
        var firstResult = await first;

        Assert.Equal(WMMembershipPaymentState.Paid, firstResult.State);
        Assert.Equal(WMMembershipPaymentState.Pending, second.State);
        Assert.Equal(1, gateway.CreateCount);
        Assert.Equal(1, launcher.CallCount);
    }

    [Fact]
    public async Task ReconcilePending_IgnoresOrderOwnedByAnotherUser()
    {
        var store = new MemoryPendingStore
        {
            ForcedRead = new WMPendingMembershipOrder("another-user", OrderId, "year", DateTime.UtcNow)
        };
        var gateway = Gateway(query: _ => Task.FromResult(PaidQuery()));
        var service = Service(gateway, new FakeLauncher(), store, AuthenticatedAccount());

        var result = await service.ReconcilePendingAsync();

        Assert.Null(result);
        Assert.Equal(0, gateway.QueryCount);
    }

    [Fact]
    public async Task DesktopPurchase_ReturnsTheOpenedPaymentUrlForTheHistoricalDialog()
    {
        Global.DeviceType = DeviceType.Mac;
        const string paymentUrl = "https://pay.example.test/desktop-order";
        var gateway = new FakeGateway(
            SuccessfulOrder(OrderInfo(OrderId)),
            _ => Task.FromResult(PendingQuery()),
            new API<DesktopPayOrder>
            {
                success = true,
                data = new DesktopPayOrder
                {
                    OutTradeNo = OrderId,
                    PayUrl = paymentUrl
                }
            });
        var external = new FakeExternalActionService();
        var service = new WMMembershipService(
            gateway,
            new FakeLauncher(),
            new MemoryPendingStore(),
            new FakeClock(),
            AuthenticatedAccount(),
            external);

        var result = await service.PurchaseAsync("year");

        Assert.Equal(WMMembershipPaymentState.Pending, result.State);
        Assert.Equal(OrderId, result.OrderId);
        Assert.Equal(paymentUrl, result.PaymentUrl);
        Assert.Equal(paymentUrl, external.LastOpenedUrl);
    }

    [Fact]
    public async Task PendingStore_AtomicallyPersistsAndDeletesCurrentUsersOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "watermark-membership-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "pending.json");
        try
        {
            var store = new WMPendingMembershipStore(path);
            var pending = new WMPendingMembershipOrder(UserId, OrderId, "year", DateTime.UtcNow);

            await store.SaveAsync(pending);
            Assert.Equal(pending, await store.GetAsync(UserId));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            await store.DeleteAsync(UserId, OrderId);
            Assert.Null(await store.GetAsync(UserId));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    public void Dispose()
    {
        Global.DeviceType = originalDeviceType;
    }

    private static WMMembershipService Service(
        FakeGateway gateway,
        FakeLauncher launcher,
        MemoryPendingStore store,
        FakeAccountService accounts,
        FakeClock? clock = null) =>
        new(gateway, launcher, store, clock ?? new FakeClock(), accounts, new FakeExternalActionService());

    private static FakeGateway Gateway(
        API<string>? create = null,
        Func<string, Task<API<DesktopPayStatus>>>? query = null) =>
        new(create ?? SuccessfulOrder(OrderInfo(OrderId)), query ?? (_ => Task.FromResult(PendingQuery())));

    private static FakeAccountService AuthenticatedAccount() => new(new WMAccountState(
        true, UserId, "Android User", "user@example.com", null, false, 0, null));

    private static FakeAccountService PaidAccountAfterRefresh()
    {
        var account = AuthenticatedAccount();
        account.StateAfterRefresh = account.State with
        {
            IsVip = true,
            ExpiresAt = DateTime.UtcNow.AddYears(1)
        };
        return account;
    }

    private static API<string> SuccessfulOrder(string orderInfo) => new()
    {
        success = true,
        data = orderInfo,
        message = new APISub { content = string.Empty }
    };

    private static API<DesktopPayStatus> PaidQuery() => Query("PAID", DateTime.UtcNow.AddMonths(1));
    private static API<DesktopPayStatus> PendingQuery() => Query("PENDING", null);

    private static API<DesktopPayStatus> Query(string status, DateTime? expireDate) => new()
    {
        success = true,
        data = new DesktopPayStatus
        {
            OutTradeNo = OrderId,
            Status = status,
            Message = status,
            ExpireDate = expireDate
        },
        message = new APISub { content = string.Empty }
    };

    private static string OrderInfo(string outTradeNo)
    {
        var bizContent = JsonSerializer.Serialize(new
        {
            subject = "轻影会员",
            out_trade_no = outTradeNo,
            total_amount = "28.00"
        });
        return $"app_id=20260001&biz_content={Uri.EscapeDataString(bizContent)}&sign=not-logged";
    }

    private sealed class FakeGateway(
        API<string> createResult,
        Func<string, Task<API<DesktopPayStatus>>> query,
        API<DesktopPayOrder>? desktopCreateResult = null) : IWMMembershipPaymentGateway
    {
        public int CreateCount { get; private set; }
        public int QueryCount { get; private set; }
        public string? CreatedForUserId { get; private set; }

        public Task<API<string>> CreateAndroidOrderAsync(
            decimal cost,
            string planName,
            string userId,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CreateCount++;
            CreatedForUserId = userId;
            return Task.FromResult(createResult);
        }

        public Task<API<DesktopPayOrder>> CreateDesktopOrderAsync(
            decimal cost,
            string planName,
            string userId,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return desktopCreateResult is null
                ? throw new InvalidOperationException("Android tests must not create desktop orders.")
                : Task.FromResult(desktopCreateResult);
        }

        public Task<API<DesktopPayStatus>> QueryAsync(
            string outTradeNo,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            QueryCount++;
            return query(outTradeNo);
        }
    }

    private sealed class FakeLauncher : IWMAlipayAppLauncher
    {
        private readonly Func<string, CancellationToken, Task<WMAlipayAppLaunchResult>> launch;

        public FakeLauncher(
            Func<string, CancellationToken, Task<WMAlipayAppLaunchResult>>? launch = null,
            WMAlipayAppLaunchResult? result = null)
        {
            this.launch = launch ?? ((_, _) => Task.FromResult(
                result ?? new WMAlipayAppLaunchResult("9000", string.Empty)));
        }

        public int CallCount { get; private set; }

        public Task<WMAlipayAppLaunchResult> LaunchAsync(
            string orderInfo,
            CancellationToken token = default)
        {
            CallCount++;
            return launch(orderInfo, token);
        }
    }

    private sealed class MemoryPendingStore : IWMPendingMembershipStore
    {
        private readonly Dictionary<string, WMPendingMembershipOrder> orders = new(StringComparer.Ordinal);
        public WMPendingMembershipOrder? ForcedRead { get; init; }

        public Task<WMPendingMembershipOrder?> GetAsync(string userId, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (ForcedRead is not null) return Task.FromResult<WMPendingMembershipOrder?>(ForcedRead);
            lock (orders)
                return Task.FromResult(orders.GetValueOrDefault(userId));
        }

        public Task SaveAsync(WMPendingMembershipOrder order, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            lock (orders) orders[order.UserId] = order;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string userId, string outTradeNo, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            lock (orders)
            {
                if (orders.TryGetValue(userId, out var order) && order.OutTradeNo == outTradeNo)
                    orders.Remove(userId);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock : IWMMembershipPaymentClock
    {
        public DateTime UtcNow { get; } = DateTime.UtcNow;
        public TimeSpan TotalDelay { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            TotalDelay += delay;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccountService(WMAccountState state) : IWMAccountService
    {
        public WMAccountState State { get; private set; } = state;
        public WMAccountState? StateAfterRefresh { get; set; }
        public int RefreshCount { get; private set; }
        public event Action? Changed;

        public Task RefreshAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            RefreshCount++;
            if (StateAfterRefresh is not null) State = StateAfterRefresh;
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public Task<WMAccountResult> LoginAsync(WMLoginRequest request, CancellationToken token) => Failure();
        public Task<WMAccountResult> RegisterAsync(WMRegisterRequest request, CancellationToken token) => Failure();
        public Task<WMAccountResult> RecoverPasswordAsync(WMRecoverPasswordRequest request, CancellationToken token) => Failure();
        public Task<WMAccountResult> ChangePasswordAsync(WMChangePasswordRequest request, CancellationToken token) => Failure();
        public Task<WMAccountResult> DeleteAccountAsync(WMDeleteAccountRequest request, CancellationToken token) => Failure();
        public Task<WMAccountResult> SendVerificationCodeAsync(string email, CancellationToken token) => Failure();
        public Task SignOutAsync() => Task.CompletedTask;

        private static Task<WMAccountResult> Failure() =>
            Task.FromResult(new WMAccountResult(false, "Not used by payment tests."));
    }

    private sealed class FakeExternalActionService : IWMExternalActionService
    {
        public string? LastOpenedUrl { get; private set; }
        public Task OpenUrlAsync(string url)
        {
            LastOpenedUrl = url;
            return Task.CompletedTask;
        }
        public Task CopyTextAsync(string text) => Task.CompletedTask;
    }
}
