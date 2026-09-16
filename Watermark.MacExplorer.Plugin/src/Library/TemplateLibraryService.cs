using Watermark.Shared.Models;

namespace Watermark.MacExplorer.Plugin.Library;

internal sealed record TemplateItem(string Id, string Name, string Directory, string? CoverPath);

/// <summary>已下载模板库：读取 ~/DFM/Templates 下带 config.json 的模板目录。</summary>
internal sealed class TemplateLibraryService
{
    public async Task<IReadOnlyList<TemplateItem>> LoadAsync(CancellationToken token)
    {
        var items = Enumerate();
        var resolved = await Task.WhenAll(items.Select(async item => item.CoverPath is null
            ? item with { CoverPath = await TemplateCoverCache.EnsureAsync(item.Id, token).ConfigureAwait(false) }
            : item)).ConfigureAwait(false);
        return resolved.OrderByDescending(item => Directory.GetLastWriteTimeUtc(item.Directory)).ToArray();
    }

    public IReadOnlyList<TemplateItem> Enumerate()
    {
        var root = Global.AppPath.TemplatesFolder;
        if (!Directory.Exists(root)) return [];
        var items = new List<TemplateItem>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(id) || id.StartsWith('.')) continue;
            var configPath = Path.Combine(directory, "config.json");
            if (!File.Exists(configPath)) continue;
            try
            {
                var canvas = Global.ReadConfigFromPath(configPath);
                var name = string.IsNullOrWhiteSpace(canvas.Name) ? id : canvas.Name.Trim();
                items.Add(new TemplateItem(id, name, directory, TemplateCoverCache.LocalCover(directory)));
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"模板 {id} 读取失败，已跳过：{ex.Message}");
            }
        }
        return items.OrderByDescending(item => Directory.GetLastWriteTimeUtc(item.Directory)).ToArray();
    }
}
