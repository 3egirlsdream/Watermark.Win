using System.Globalization;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Watermark.Shared.Models;

namespace Watermark.Web.Services;

public static class WMWebsiteLoginEndpoints
{
    public sealed record Credentials(string User, string Pwd);
    public sealed record Redemption(string Ticket);
    public sealed record BrowserCredentials(string User, string Password);
    public sealed record PlanRequest(string PlanId);
    public sealed record OrderRequest(string OutTradeNo);
    private const string BackendSessionClaim = "website-session";
    private static bool SameOrigin(HttpContext context) => string.Equals(context.Request.Headers.Origin, "https://thankful.top", StringComparison.Ordinal);

    public static void MapWebsiteLogin(this WebApplication app)
    {
        app.MapPost("/account/client-session", async (Credentials request,
            WMWebsiteLoginTickets tickets, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!ValidCredentials(request.User, request.Pwd)) return Results.BadRequest();
            var result = await WMWebsiteBackend.Login(request.User, request.Pwd, context.RequestAborted);
            if (result is null || string.IsNullOrWhiteSpace(result.Account?.ID) || string.IsNullOrEmpty(result.Token))
                return Results.Unauthorized();
            var ticket = tickets.Issue(result.Account, result.Token);
            // The fragment is never sent in HTTP requests or Referer headers.
            return Results.Json(new { url = $"https://thankful.top/account/client-login#ticket={ticket}" });
        }).RequireRateLimiting("account");

        app.MapPost("/account/client-session/redeem", async (Redemption request,
            WMWebsiteLoginTickets tickets, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!SameOrigin(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var session = tickets.RedeemSession(request.Ticket);
            if (session is null) return Results.Unauthorized();
            await SignIn(context, session.Account, session.Token);
            return Results.NoContent();
        }).RequireRateLimiting("account");

        app.MapPost("/account/login", async (BrowserCredentials request, APIHelper api, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!SameOrigin(context)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(request.User) || request.User.Length > 254
                || string.IsNullOrEmpty(request.Password) || request.Password.Length > 128) return Results.BadRequest();
            var session = await WMWebsiteBackend.Login(request.User, api.GetMD5(request.Password), context.RequestAborted);
            if (session is null) return Results.Unauthorized();
            await SignIn(context, session.Account, session.Token);
            return Results.NoContent();
        }).RequireRateLimiting("account");

        app.MapPost("/account/membership/order", async (PlanRequest request, HttpContext context) =>
            await ProxyPayment(context, "WebsiteCreateOrder", request)).RequireRateLimiting("account");
        app.MapPost("/account/membership/status", async (OrderRequest request, HttpContext context) =>
            await ProxyPayment(context, "WebsiteQueryOrder", request)).RequireRateLimiting("account");

        app.MapGet("/account/client-login", (HttpContext context, IWebHostEnvironment environment) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'";
            return Results.File(Path.Combine(environment.WebRootPath, "client-login.html"), "text/html; charset=utf-8");
        }).RequireRateLimiting("account");

        app.MapPost("/account/sign-out", async (HttpContext context) =>
        {
            if (!SameOrigin(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var token = context.User.FindFirstValue(BackendSessionClaim);
            if (token != null)
            {
                using var response = await WMWebsiteBackend.Send("WebsiteLogout", new { }, token, context.RequestAborted);
                response.EnsureSuccessStatusCode();
            }
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Redirect("/");
        }).RequireRateLimiting("account");
    }
    private static bool ValidCredentials(string user, string pwd)
    {
        if (string.IsNullOrWhiteSpace(user) || user.Length > 254 || string.IsNullOrEmpty(pwd) || pwd.Length > 1024) return false;
        try { return Convert.FromBase64String(pwd).Length is > 0 and <= 512; }
        catch (FormatException) { return false; }
    }

    private static async Task SignIn(HttpContext context, WMLoginChildModel account, string token)
    {
        if (string.IsNullOrEmpty(account.ID) || string.IsNullOrEmpty(token)) throw new InvalidOperationException("无效登录响应");
        var claims = new[] {
            new Claim(ClaimTypes.NameIdentifier, account.ID),
            new Claim(ClaimTypes.Name, account.DISPLAY_NAME ?? "轻影用户"),
            new Claim(ClaimTypes.Email, account.USER_NAME ?? string.Empty),
            new Claim("expires", account.EXPIRE_DATE?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
            new Claim(BackendSessionClaim, token)
        };
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) });
    }

    private static async Task<IResult> ProxyPayment(HttpContext context, string action, object body)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!SameOrigin(context)) return Results.StatusCode(403);
        var token = context.User.FindFirstValue(BackendSessionClaim);
        if (context.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(token)) return Results.Unauthorized();
        using var response = await WMWebsiteBackend.Send(action, body, token, context.RequestAborted);
        if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
        // The backend returns only order data; never forward authentication headers or tokens.
        var json = await response.Content.ReadAsStringAsync(context.RequestAborted);
        if (action == "WebsiteQueryOrder")
        {
            var status = JsonSerializer.Deserialize<DesktopPayStatus>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (status?.Status == "PAID" && status.ExpireDate.HasValue)
            {
                // Refresh public display data without extending the authenticated session lifetime.
                var auth = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                var identity = (ClaimsIdentity)context.User.Identity!;
                var previous = identity.FindFirst("expires");
                if (previous != null) identity.RemoveClaim(previous);
                identity.AddClaim(new Claim("expires", status.ExpireDate.Value.ToString("O", CultureInfo.InvariantCulture)));
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, context.User, auth.Properties);
            }
        }
        return action == "WebsiteCreateOrder"
            ? Results.Json(JsonSerializer.Deserialize<DesktopPayOrder>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            : Results.Json(JsonSerializer.Deserialize<DesktopPayStatus>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

}
