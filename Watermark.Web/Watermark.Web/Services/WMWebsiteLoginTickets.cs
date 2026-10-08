using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Watermark.Shared.Models;

namespace Watermark.Web.Services;

// The browser receives only random one-use ticket bytes. Credentials are never retained.
// The scoped backend session stays server-side until placed in a protected HttpOnly cookie.
public sealed class WMWebsiteLoginTickets(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> tickets = new(StringComparer.Ordinal);
    public sealed record Session(WMLoginChildModel Account, string Token);
    private sealed record Entry(Session Session, DateTimeOffset ExpiresAt);

    public string Issue(WMLoginChildModel account, string token = "")
    {
        lock (gate)
        {
            foreach (var key in tickets.Where(pair => pair.Value.ExpiresAt <= clock.GetUtcNow())
                         .Select(pair => pair.Key).ToArray()) tickets.Remove(key);
            if (tickets.Count >= 1024) throw new InvalidOperationException("请稍后重试。");
            var ticket = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            tickets.Add(ticket, new Entry(new Session(account, token), clock.GetUtcNow().AddMinutes(1)));
            return ticket;
        }
    }

    public WMLoginChildModel? Redeem(string ticket) => RedeemSession(ticket)?.Account;

    public Session? RedeemSession(string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length != 43) return null;
        lock (gate)
        {
            return tickets.Remove(ticket, out var entry) && entry.ExpiresAt > clock.GetUtcNow()
                ? entry.Session : null;
        }
    }
}
