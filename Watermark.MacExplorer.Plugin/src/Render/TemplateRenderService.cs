using MacExplorer.PluginSdk;
using SkiaSharp;
using Watermark.MacExplorer.Plugin.Library;
using Watermark.Shared.Models;

namespace Watermark.MacExplorer.Plugin.Render;

internal sealed record RenderOutcome(IReadOnlyList<PluginOutput> Outputs, IReadOnlyList<string> Warnings);

/// <summary>逐张套用模板渲染，产物写入宿主工作目录，由宿主搬运到源照片目录。</summary>
internal sealed class TemplateRenderService
{
    private readonly HashSet<string> fontsReady = [];

    public async Task PrepareFontsAsync(TemplateItem template, CancellationToken token)
    {
        if (!fontsReady.Add(template.Id)) return;
        try
        {
            var canvas = Global.ReadConfigFromPath(Path.Combine(template.Directory, "config.json"));
            await Global.InitFonts([canvas]).WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"模板 {template.Id} 字体准备失败，将使用本地字体回退：{ex.Message}");
        }
    }

    public async Task<RenderOutcome> RenderAsync(
        TemplateItem template,
        IReadOnlyList<string> sources,
        string workDirectory,
        IProgress<PluginProgress>? progress,
        CancellationToken token)
    {
        var configPath = Path.Combine(template.Directory, "config.json");
        var batch = sources.Count > 1;
        var outputs = new List<PluginOutput>();
        var warnings = new List<string>();
        for (var index = 0; index < sources.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var source = sources[index];
            var name = Path.GetFileName(source);
            progress?.Report(new PluginProgress(
                batch ? $"正在生成 {index + 1}/{sources.Count}：{name}" : $"正在生成 {name}",
                index * 100.0 / sources.Count)
            {
                // 批量才占用宿主后台任务面板，单张只显示状态栏文字。
                ShowInTaskPanel = batch,
                TaskTitle = batch ? $"轻影水印 · 生成 {sources.Count} 张照片" : null
            });
            try
            {
                var bytes = await RenderOneAsync(configPath, source, token).ConfigureAwait(false);
                // 每个文件独立子目录，同名产物互不覆盖；宿主按 SourcePath 逐个搬回源照片目录。
                var folder = Path.Combine(workDirectory, index.ToString());
                Directory.CreateDirectory(folder);
                var temporary = Path.Combine(folder, $"litograph-{index:00}.jpg");
                await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
                outputs.Add(new PluginOutput(temporary, SuggestedName(source, template.Name)) { SourcePath = source });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                PluginLog.Error($"生成失败：{source}", ex);
                warnings.Add($"{Path.GetFileName(source)}：{ex.Message}");
            }
        }
        progress?.Report(new PluginProgress(outputs.Count > 0 ? $"已生成 {outputs.Count} 张照片" : "未能生成任何照片", 100)
        {
            ShowInTaskPanel = batch,
            TaskTitle = batch ? $"轻影水印 · 生成 {sources.Count} 张照片" : null
        });
        return new RenderOutcome(outputs, warnings);
    }

    private static async Task<byte[]> RenderOneAsync(string configPath, string source, CancellationToken token)
    {
        // 每张都重新读取模板：渲染过程会改写画布状态，复用同一实例会串图。
        var canvas = Global.ReadConfigFromPath(configPath);
        canvas.Path = source;
        var bytes = await new WatermarkHelper().RenderPureAsync(new WMTemplateRenderRequest(
            canvas, false, SKEncodedImageFormat.Jpeg, PluginEnvironment.ExportQuality), token).ConfigureAwait(false);
        if (bytes is not { Length: > 0 }) throw new InvalidDataException("无法读取这张照片，或模板没有可用的主图位置。");
        return bytes;
    }

    /// <summary>输出名称形如「原文件名_模板名.jpg」；宿主遇到同名文件会自动追加序号。</summary>
    public static string SuggestedName(string sourcePath, string templateName)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var suffix = "_" + Sanitize(templateName);
        // 文件名按 UTF-8 字节数限制，中文名也需要留出宿主追加「 2」和扩展名的余量。
        var budget = MaxNameBytes - System.Text.Encoding.UTF8.GetByteCount(suffix + ".jpg");
        return TrimToBytes(Sanitize(stem), Math.Max(budget, 16)) + suffix + ".jpg";
    }

    private static string TrimToBytes(string value, int maxBytes)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;
        var length = value.Length;
        while (length > 0 && System.Text.Encoding.UTF8.GetByteCount(value.AsSpan(0, length)) > maxBytes) length--;
        return value[..length].TrimEnd();
    }

    private const int MaxNameBytes = 200;

    private static string Sanitize(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(char.IsControl(ch) || InvalidNameChars.Contains(ch) ? '_' : ch);
        var result = builder.ToString().Trim().Trim('.');
        return result.Length == 0 ? "照片" : result;
    }

    private static readonly char[] InvalidNameChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];
}
