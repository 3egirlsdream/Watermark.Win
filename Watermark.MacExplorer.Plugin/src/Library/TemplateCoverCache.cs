using Watermark.Shared.Models;

namespace Watermark.MacExplorer.Plugin.Library;

/// <summary>模板封面缓存：模板自带 default.jpg 优先，否则回源 CDN 并落盘供后续复用。</summary>
internal static class TemplateCoverCache
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Gate = new(4, 4);

    private static string Folder => Path.Combine(Global.AppPath.BasePath, "Cache", "mac-explorer-plugin", "covers");

    public static string? LocalCover(string templateDirectory)
    {
        foreach (var name in LocalCoverNames)
        {
            var candidate = Path.Combine(templateDirectory, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static async Task<string?> EnsureAsync(string templateId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return null;
        var target = Path.Combine(Folder, templateId + ".jpg");
        if (IsUsable(target)) return target;

        await Gate.WaitAsync(token).ConfigureAwait(false);
        var staging = target + ".part";
        try
        {
            if (IsUsable(target)) return target;
            Directory.CreateDirectory(Folder);
            using var response = await Client.GetAsync(Global.GetSrc(templateId), HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = File.Create(staging))
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            if (new FileInfo(staging).Length == 0) return null;
            File.Move(staging, target, true);
            return target;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"模板 {templateId} 封面获取失败：{ex.Message}");
            return null;
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
            Gate.Release();
        }
    }

    private static bool IsUsable(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    private static readonly string[] LocalCoverNames = ["default.jpg", "default.jpeg", "default.png", "cover.jpg"];
}
