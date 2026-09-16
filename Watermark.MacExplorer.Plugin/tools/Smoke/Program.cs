using System.Diagnostics;
using MacExplorer.PluginSdk;
using SkiaSharp;
using Watermark.MacExplorer.Plugin;
using Watermark.MacExplorer.Plugin.Library;
using Watermark.MacExplorer.Plugin.Market;
using Watermark.MacExplorer.Plugin.Render;
using Watermark.Shared.Models;

namespace Watermark.Smoke;

/// <summary>
/// 不依赖宿主进程的冒烟：初始化 → 枚举模板 → 授权状态 → prepare 校验 → 市场 → 真实渲染并校验产物。
/// 用法：dotnet run --project tools/Smoke -c Release -- [--photo &lt;照片&gt;]
///       [--template &lt;模板ID&gt;] [--download &lt;市场模板ID&gt;] [--keep]
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var photo = Value(args, "--photo");
        var templateId = Value(args, "--template");
        var downloadId = Value(args, "--download");
        var keep = args.Contains("--keep");
        var failures = new List<string>();

        Console.WriteLine("轻影水印插件 · 无头冒烟");
        Console.WriteLine();

        PluginEnvironment.EnsureInitialized();
        Section("1. 环境");
        Line("数据目录", Global.AppPath.BasePath);
        Line("模板目录", Global.AppPath.TemplatesFolder);
        Line("输出质量", PluginEnvironment.ExportQuality.ToString());

        Section("2. 已下载模板");
        var library = new TemplateLibraryService();
        var templates = await library.LoadAsync(CancellationToken.None);
        Line("模板数量", templates.Count.ToString());
        foreach (var item in templates)
            Line("  · " + item.Name, $"{item.Id}  封面：{(item.CoverPath is null ? "缺失" : Path.GetFileName(item.CoverPath))}");

        var template = templateId is null
            ? templates.FirstOrDefault()
            : templates.FirstOrDefault(item => item.Id == templateId);
        if (template is null)
            Line("选用模板", templateId is null ? "（模板库为空，跳过渲染检查）" : $"未找到 {templateId}");

        var source = photo ?? (template is null ? null : Path.Combine(template.Directory, "default.jpg"));
        if (source is not null && !File.Exists(source))
        {
            failures.Add($"找不到用于冒烟的照片：{source}");
            source = null;
        }

        var work = Path.Combine(Path.GetTempPath(), "litograph-smoke", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        var invocation = new PluginInvocation("smoke", "generate",
            [new PluginFile(source ?? Path.Combine(work, "missing.jpg"))], work);

        Section("3. 授权状态");
        var entry = new PluginEntry();
        foreach (var (label, trial) in new[]
                 {
                     ("未开始试用", new PluginTrial()),
                     ("试用进行中", new PluginTrial(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(6), DateTimeOffset.Now))
                 })
        {
            try
            {
                var result = await entry.CheckAccessAsync(new PluginAccessRequest(invocation, trial), CancellationToken.None);
                Line(label, $"{result.Status}：{result.Message}");
            }
            catch (Exception ex)
            {
                failures.Add($"授权检查（{label}）抛出异常：{ex.Message}");
                Line(label, "异常：" + ex.Message);
            }
        }

        Section("4. prepare 校验");
        try
        {
            var preparation = await entry.PrepareAsync(invocation, CancellationToken.None);
            Line("合法输入", preparation.Configuration is null ? "通过（无附加配置窗口）" : "意外要求配置窗口");
            if (preparation.Configuration is not null) failures.Add("prepare 不应返回配置窗口。");
        }
        catch (Exception ex)
        {
            failures.Add("合法输入被 prepare 拒绝：" + ex.Message);
            Line("合法输入", "被拒绝：" + ex.Message);
        }
        foreach (var (label, files) in new[]
                 {
                     ("无文件", Array.Empty<PluginFile>()),
                     ("错误的扩展名", new[] { new PluginFile(Path.ChangeExtension(source ?? "x", ".txt")!) }),
                     ("超过数量上限", Enumerable.Range(0, 101).Select(_ => new PluginFile(source ?? "x")).ToArray())
                 })
        {
            var probe = invocation with { Files = files };
            try
            {
                await entry.PrepareAsync(probe, CancellationToken.None);
                failures.Add($"prepare 未拒绝异常输入：{label}");
                Line(label, "未拒绝（应为错误）");
            }
            catch (Exception ex)
            {
                Line(label, "已拒绝：" + ex.Message);
            }
        }

        Section("5. 模板市场");
        var market = new TemplateMarketService();
        try
        {
            var first = await market.SearchAsync(null, 0, CancellationToken.None);
            Line("首页条目", $"{first.Items.Count} 条（上限 {TemplateMarketService.PageSize}）");
            foreach (var item in first.Items)
                Line("  · " + item.Name, $"{item.Id}  下载 {item.DownloadTimes}  封面 {(item.CoverPath is null ? "缺失" : "已缓存")}");
            if (first.Items.Count > TemplateMarketService.PageSize) failures.Add("首页条目超过分页上限。");
            if (first.HasMore && first.Cursor > 0)
            {
                var second = await market.SearchAsync(null, first.Cursor, CancellationToken.None);
                Line("第二页", $"{second.Items.Count} 条，与首页重复 {first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)).Count()} 条");
            }
            else if (first.HasMore)
            {
                failures.Add("还有更多模板，但没有返回游标。");
            }

            var keywordPage = await market.SearchAsync("水印", 0, CancellationToken.None);
            Line("搜索「水印」", $"{keywordPage.Items.Count} 条");

            var target = downloadId is null ? null : first.Items.Concat((await market.SearchAsync(null, first.Cursor, CancellationToken.None)).Items)
                .FirstOrDefault(item => item.Id == downloadId);
            if (downloadId is not null)
            {
                if (target is null) Line("下载模板", $"未在市场中找到 {downloadId}");
                else
                {
                    var outcome = await market.DownloadAsync(target, CancellationToken.None);
                    Line("下载模板", $"{target.Name} → {(outcome.Succeeded ? "成功" : "失败")}：{outcome.Message}");
                    if (!outcome.Succeeded) failures.Add("模板下载失败：" + outcome.Message);
                    else
                    {
                        templates = await library.LoadAsync(CancellationToken.None);
                        template = templates.FirstOrDefault(item => item.Id == downloadId) ?? template;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add("模板市场不可用：" + ex.Message);
            Line("模板市场", "失败：" + ex.Message);
        }

        if (template is not null && source is not null)
        {
            Section("6. 渲染");
            var sourceBytes = await File.ReadAllBytesAsync(source);
            Line("源照片", $"{sourceBytes.Length / 1024.0:0.0} KB，EXIF {(HasExif(sourceBytes) ? "有" : "无")}");
            var renderer = new TemplateRenderService();
            var watch = Stopwatch.StartNew();
            var outcome = await renderer.RenderAsync(template, [source], work, null, CancellationToken.None);
            watch.Stop();
            Line("耗时", $"{watch.ElapsedMilliseconds} ms");
            Line("产物数量", outcome.Outputs.Count.ToString());
            foreach (var warning in outcome.Warnings) Line("警告", warning);
            if (outcome.Outputs.Count == 0) failures.Add("渲染未产出任何文件。");

            foreach (var output in outcome.Outputs)
            {
                var bytes = await File.ReadAllBytesAsync(output.Path);
                try
                {
                    Line("文件名", RenderName(output.SuggestedName));
                }
                catch (InvalidDataException ex)
                {
                    failures.Add(ex.Message);
                    Line("文件名", "不合法：" + output.SuggestedName);
                }
                Line("大小", $"{bytes.Length / 1024.0:0.0} KB");
                if (bytes.Length < 1024) failures.Add("产物过小，可能不是有效图片。");
                if (!IsJpeg(bytes)) failures.Add("产物不是 JPEG。");
                using var codec = SKCodec.Create(new MemoryStream(bytes));
                if (codec is null) failures.Add("产物无法解码。");
                else Line("尺寸", $"{codec.Info.Width}×{codec.Info.Height}，EXIF {(HasExif(bytes) ? "已保留" : "缺失")}");
            }
            Line("工作目录", work);

            Section("7. 批量");
            try
            {
                var second = Path.Combine(work, "batch-2.jpg");
                File.Copy(source, second, true);
                string[] batchSources = [source, second];
                var collector = new ProgressCollector();
                var batch = await new TemplateRenderService().RenderAsync(template, batchSources, work, collector, CancellationToken.None);
                Line("产物数量", $"{batch.Outputs.Count}（期望 2）");
                if (batch.Outputs.Count != 2) failures.Add("批量渲染未按选中文件数逐个产出。");
                var folders = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < batch.Outputs.Count; index++)
                {
                    var output = batch.Outputs[index];
                    var folder = Path.GetDirectoryName(Path.GetFullPath(output.Path))!;
                    folders.Add(folder);
                    Line($"输出 {index + 1}", $"SourcePath {Describe(output.SourcePath, batchSources[index])}  目录 {Path.GetRelativePath(work, folder)}");
                    if (output.SourcePath != batchSources[index]) failures.Add($"第 {index + 1} 个输出未用 SourcePath 指回对应源文件。");
                    if (!folder.StartsWith(Path.GetFullPath(work) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        failures.Add("批量输出落在宿主工作目录之外。");
                    else if (!File.Exists(output.Path)) failures.Add("批量输出文件不存在。");
                }
                if (folders.Count != batch.Outputs.Count) failures.Add("批量输出共用工作子目录，同名产物会互相覆盖。");
                Line("进度上报", $"{collector.Items.Count} 条，其中任务面板 {collector.Items.Count(item => item.ShowInTaskPanel)} 条");
                if (collector.Items.Count < batch.Outputs.Count) failures.Add("批量渲染没有逐文件上报进度。");
                if (collector.Items.Any(item => !item.ShowInTaskPanel)) failures.Add("批量渲染的进度未声明后台任务面板。");
                Line("任务标题", collector.Items.FirstOrDefault(item => item.TaskTitle is not null)?.TaskTitle ?? "（未设置）");

                var single = new ProgressCollector();
                await new TemplateRenderService().RenderAsync(template, [source], work, single, CancellationToken.None);
                if (single.Items.Any(item => item.ShowInTaskPanel)) failures.Add("单张渲染不应占用后台任务面板。");
                Line("单张进度", $"{single.Items.Count} 条，任务面板 {single.Items.Count(item => item.ShowInTaskPanel)} 条");
            }
            catch (Exception ex)
            {
                failures.Add("批量渲染失败：" + ex.Message);
                Line("批量", "失败：" + ex.Message);
            }
        }

        if (!keep && Directory.Exists(work)) Directory.Delete(work, true);

        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine("冒烟通过。");
            return 0;
        }
        Console.WriteLine($"冒烟发现 {failures.Count} 个问题：");
        foreach (var failure in failures) Console.WriteLine("  ✗ " + failure);
        return 1;
    }

    /// <summary>同步收集进度，避免 Progress&lt;T&gt; 的回调线程池投递导致断言读到未到达的上报。</summary>
    private sealed class ProgressCollector : IProgress<PluginProgress>
    {
        public List<PluginProgress> Items { get; } = [];

        public void Report(PluginProgress value) => Items.Add(value);
    }

    private static string Describe(string? actual, string expected) =>
        actual == expected ? "正确" : actual is null ? "缺失" : "指向了 " + actual;

    /// <summary>复刻宿主对 SuggestedName 的校验规则，提前发现非法输出名。</summary>
    private static string RenderName(string suggestedName)
    {
        if (string.IsNullOrWhiteSpace(suggestedName) || suggestedName is "." or ".."
            || Path.GetFileName(suggestedName) != suggestedName || suggestedName.Contains('\\'))
            throw new InvalidDataException($"建议文件名不合法：{suggestedName}");
        return suggestedName;
    }

    private static bool IsJpeg(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[^2] == 0xFF && bytes[^1] == 0xD9;

    private static bool HasExif(byte[] bytes)
    {
        var marker = "Exif\0\0"u8;
        return bytes.AsSpan().IndexOf(marker) >= 0;
    }

    private static void Section(string title) => Console.WriteLine($"── {title} ──");

    private static void Line(string label, string value) => Console.WriteLine($"  {label,-12} {value}");

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
