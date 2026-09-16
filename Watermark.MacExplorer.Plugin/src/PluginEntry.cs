using MacExplorer.PluginSdk;
using MacExplorer.PluginUi;
using Watermark.MacExplorer.Plugin.Access;
using Watermark.MacExplorer.Plugin.Ui;

namespace Watermark.MacExplorer.Plugin;

/// <summary>轻影水印插件入口：授权检查、账号窗口与一体化模板面板。</summary>
public sealed class PluginEntry : IFileActionPlugin, IPluginAccessProvider
{
    // 宿主一次调用最多接收 100 个输出，插件每张照片产出一个，故两者取同一个上限。
    private const int MaxSelection = 100;

    private static readonly string[] Commands = ["market", "generate"];
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly PluginAccessService access = new();

    public Task<PluginAccessResult> CheckAccessAsync(PluginAccessRequest request, CancellationToken cancellationToken)
        => access.CheckAsync(request, cancellationToken);

    public async Task<PluginInteractionResult> ShowAccountAsync(PluginInteractionRequest request, CancellationToken cancellationToken)
    {
        PluginEnvironment.EnsureInitialized();
        var manage = string.Equals(request.Purpose, "manage", StringComparison.OrdinalIgnoreCase);
        var completed = await PluginWindows.ShowAsync(() => new AccountWindow(manage, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        PluginLog.Info(completed ? "账号窗口已完成。" : "账号窗口被关闭，视为取消。");
        return new PluginInteractionResult(completed);
    }

    public Task<PluginPreparation> PrepareAsync(PluginInvocation invocation, CancellationToken cancellationToken)
    {
        PluginEnvironment.EnsureInitialized();
        Validate(invocation);
        return Task.FromResult(new PluginPreparation());
    }

    public async Task<PluginResult> ExecuteAsync(PluginInvocation invocation, IProgress<PluginProgress> progress,
        CancellationToken cancellationToken)
    {
        PluginEnvironment.EnsureInitialized();
        Validate(invocation);
        PluginLog.Info($"命令 {invocation.CommandId} 开始处理 {invocation.Files.Length} 个文件。");

        WatermarkPanelWindow? panel = null;
        var completed = await PluginWindows.ShowAsync(() =>
        {
            panel = new WatermarkPanelWindow(invocation, progress, cancellationToken);
            return panel;
        }, cancellationToken).ConfigureAwait(false);

        if (!completed || panel is null || !panel.Completed) throw new OperationCanceledException();
        var result = panel.Result;
        PluginLog.Info($"生成完成：{result.Outputs.Count} 个文件，{result.Warnings.Count} 条警告。");
        return new PluginResult(result.Outputs.ToArray(), result.Warnings.ToArray());
    }

    private static void Validate(PluginInvocation invocation)
    {
        if (!Commands.Contains(invocation.CommandId)) throw new InvalidDataException("不支持的命令。");
        var files = invocation.Files.Where(file => !file.IsDirectory).ToArray();
        if (files.Length == 0) throw new InvalidDataException("请先选择要处理的照片。");
        if (files.Length > MaxSelection) throw new InvalidDataException($"一次最多处理 {MaxSelection} 张照片。");
        foreach (var file in files)
        {
            if (file.Source != "local") throw new InvalidDataException("仅支持本地照片。");
            var extension = Path.GetExtension(file.Path);
            if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"暂不支持 {extension} 格式，请选择 JPG、PNG 或 WebP 照片。");
            if (!File.Exists(file.Path)) throw new InvalidDataException($"文件不存在：{Path.GetFileName(file.Path)}");
        }
    }
}
