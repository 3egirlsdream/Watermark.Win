using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Watermark.Shared.Models;
using Watermark.Web.Services;
using Xunit;

namespace Watermark.Razor.Tests;

[Collection(WMMembershipPaymentCollection.CollectionName)]
public sealed class WMWebsiteLoginEndpointsTests
{
    [Fact]
    public async Task LoginHandoff_UsesPostCredentialsAndSecureCookie_AndRejectsReplayAndCrossOrigin()
    {
        var originalHost = APIHelper.HOST;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<APIHelper>();
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("account", limiter => {
            limiter.PermitLimit = 100; limiter.Window = TimeSpan.FromMinutes(1);
        }));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<WMWebsiteLoginTickets>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "__Host-Litograph.Account";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
            });
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.MapWebsiteLogin();
        var backendCalls = 0;
        app.MapPost("/api/Watermark/WebsiteLogin", async (HttpContext context) =>
        {
            Assert.False(context.Request.QueryString.HasValue);
            var body = await context.Request.ReadFromJsonAsync<WMWebsiteLoginEndpoints.Credentials>();
            Assert.Equal("account@example.test", body!.User);
            Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("test-password")), body.Pwd);
            backendCalls++;
            return Results.Json(new WMWebsiteBackend.LoginResult(
                new WMLoginChildModel { ID = "user-1", USER_NAME = body.User, DISPLAY_NAME = "Test User" }, "backend-secret-session"));
        });
        var orderCalls = 0;
        app.MapPost("/api/Watermark/WebsiteCreateOrder", async (HttpContext context) =>
        {
            Assert.Equal("backend-secret-session", context.Request.Headers["X-Website-Session"].ToString());
            using var body = await JsonDocument.ParseAsync(context.Request.Body);
            Assert.Equal("year", body.RootElement.GetProperty("planId").GetString());
            Assert.False(body.RootElement.TryGetProperty("userId", out _));
            Assert.False(body.RootElement.TryGetProperty("cost", out _));
            orderCalls++;
            return Results.Json(new DesktopPayOrder { OutTradeNo = "order-1", PayUrl = "https://thankful.top/api/Watermark/OpenDesktopPay?outTradeNo=order-1" }, new JsonSerializerOptions { PropertyNamingPolicy = null });
        });
        app.MapGet("/test/account", (HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous");

        try
        {
            await app.StartAsync();
            APIHelper.HOST = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(APIHelper.HOST) };
            using var issue = await http.PostAsJsonAsync("/account/client-session",
                new { user = "account@example.test", pwd = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("test-password")) });
            Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
            Assert.Equal("no-store", issue.Headers.CacheControl!.ToString());
            using var json = JsonDocument.Parse(await issue.Content.ReadAsStringAsync());
            var url = new Uri(json.RootElement.GetProperty("url").GetString()!);
            Assert.Equal("https", url.Scheme);
            Assert.Empty(url.Query);
            Assert.DoesNotContain("account@example.test", url.AbsoluteUri);
            var ticket = url.Fragment["#ticket=".Length..];

            using var forbidden = await http.PostAsJsonAsync("/account/client-session/redeem", new { ticket });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            http.DefaultRequestHeaders.Add("Origin", "https://thankful.top");
            using var redeem = await http.PostAsJsonAsync("/account/client-session/redeem", new { ticket });
            Assert.Equal(HttpStatusCode.NoContent, redeem.StatusCode);
            var cookie = redeem.Headers.GetValues("Set-Cookie").Single();
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
            http.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
            Assert.Equal("user-1", await http.GetStringAsync("/test/account"));
            using var replay = await http.PostAsJsonAsync("/account/client-session/redeem", new { ticket });
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            Assert.DoesNotContain("backend-secret-session", cookie);
            using var order = await http.PostAsJsonAsync("/account/membership/order", new { planId = "year", userId = "attacker", cost = 0.01 });
            Assert.Equal(HttpStatusCode.OK, order.StatusCode);
            Assert.Equal(1, orderCalls);
            using var orderBody = JsonDocument.Parse(await order.Content.ReadAsStringAsync());
            Assert.Equal("order-1", orderBody.RootElement.GetProperty("outTradeNo").GetString());
            http.DefaultRequestHeaders.Remove("Origin");
            using var crossOriginOrder = await http.PostAsJsonAsync("/account/membership/order", new { planId = "year" });
            Assert.Equal(HttpStatusCode.Forbidden, crossOriginOrder.StatusCode);
            Assert.Equal(1, orderCalls);
            http.DefaultRequestHeaders.Remove("Cookie");
            http.DefaultRequestHeaders.Add("Origin", "https://thankful.top");
            using var anonymousOrder = await http.PostAsJsonAsync("/account/membership/order", new { planId = "year" });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousOrder.StatusCode);
            using var browserLogin = await http.PostAsJsonAsync("/account/login", new { user = "account@example.test", password = "test-password" });
            Assert.Equal(HttpStatusCode.NoContent, browserLogin.StatusCode);
            Assert.Equal(2, backendCalls);
        }
        finally
        {
            APIHelper.HOST = originalHost;
            await app.StopAsync();
        }
    }
}
