using System.Runtime.InteropServices;
using SkiaSharp;
using Watermark.Shared.Enums;
using Watermark.Shared.Models;

namespace Watermark.MacExplorer.Plugin;

internal static class PluginEnvironment
{
    /// <summary>导出 JPEG 的质量，视觉接近无损。</summary>
    public const int ExportQuality = 95;

    private static int initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref initialized, 1) == 1) return;
        try
        {
            // Global.AppPath 在首次访问时按 DeviceType 决定数据目录，必须先设定平台。
            Global.DeviceType = DeviceType.Mac;
            Global.Resolution = "default";
            Global.Quality = ExportQuality;
            RegisterNativeLibraryResolver();
            PluginLog.Info($"轻影水印插件已初始化，数据目录：{Global.AppPath.BasePath}");
        }
        catch (Exception ex)
        {
            PluginLog.Error("插件初始化失败", ex);
        }
    }

    /// <summary>
    /// 宿主按插件目录解析托管依赖，这里为 SkiaSharp 的原生库补一条同目录回退。
    /// 返回零表示交回宿主的加载上下文解析。
    /// </summary>
    private static void RegisterNativeLibraryResolver()
    {
        var directory = Path.GetDirectoryName(typeof(PluginEntry).Assembly.Location);
        if (string.IsNullOrEmpty(directory)) return;
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(SKBitmap).Assembly, (name, _, _) =>
            {
                foreach (var candidate in NativeCandidates(directory, name))
                {
                    if (File.Exists(candidate)) return NativeLibrary.Load(candidate);
                }
                PluginLog.Warning($"未能从插件目录解析原生库 {name}，交由宿主加载上下文处理。");
                return IntPtr.Zero;
            });
        }
        catch (Exception ex)
        {
            PluginLog.Warning("注册原生库解析器失败，继续使用宿主默认解析：" + ex.Message);
        }
    }

    private static IEnumerable<string> NativeCandidates(string directory, string name)
    {
        var file = Path.GetFileName(name);
        if (!file.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)) file += ".dylib";
        yield return Path.Combine(directory, file);
        if (!file.StartsWith("lib", StringComparison.Ordinal))
            yield return Path.Combine(directory, "lib" + file);
    }
}
