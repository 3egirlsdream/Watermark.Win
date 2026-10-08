using System.Net;
using System.Net.Http.Json;
using Watermark.Shared.Models;

namespace Watermark.Web.Services;

public static class WMWebsiteBackend
{
    // No redirects: credentials and session headers must never follow a different origin.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public sealed record LoginResult(WMLoginChildModel Account, string Token);
    public static async Task<LoginResult?> Login(string user, string pwd, CancellationToken cancellation)
    {
        using var response = await Send("WebsiteLogin", new { user, pwd }, null, cancellation);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResult>(cancellationToken: cancellation);
    }
    public static async Task<HttpResponseMessage> Send(string action, object body, string? token, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, APIHelper.HOST.TrimEnd('/') + "/api/Watermark/" + action);
        request.Content = System.Net.Http.Json.JsonContent.Create(body);
        if (token != null) request.Headers.Add("X-Website-Session", token);
        return await Http.SendAsync(request, cancellation);
    }
}
