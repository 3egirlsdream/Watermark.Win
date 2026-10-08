using Watermark.Shared.Models;
using Watermark.Web.Services;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWebsiteLoginTicketsTests
{
    [Fact]
    public void Tickets_AreRandomSingleUseAndExpireAfterOneMinute()
    {
        var clock = new Clock();
        var tickets = new WMWebsiteLoginTickets(clock);
        var account = new WMLoginChildModel { ID = "user", USER_NAME = "user@example.test" };
        var first = tickets.Issue(account);
        var second = tickets.Issue(account);
        Assert.NotEqual(first, second);
        Assert.Equal(43, first.Length);
        Assert.Same(account, tickets.Redeem(first));
        Assert.Null(tickets.Redeem(first));
        Assert.Null(tickets.Redeem("invalid"));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Null(tickets.Redeem(second));
    }

    [Fact]
    public async Task ConcurrentRedemption_OnlyOneBrowserReceivesAccount()
    {
        var tickets = new WMWebsiteLoginTickets(TimeProvider.System);
        var ticket = tickets.Issue(new WMLoginChildModel { ID = "user" });
        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => tickets.Redeem(ticket))));
        Assert.Single(results.Where(value => value is not null));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
