namespace Watermark.MacExplorer.Plugin;

/// <summary>插件进程没有独立的日志文件，宿主会把标准错误收进插件日志。</summary>
internal static class PluginLog
{
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("info", message);

    public static void Warning(string message) => Write("warn", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("error", exception is null ? message : $"{message} :: {exception}");

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        try
        {
            lock (Gate) Console.Error.WriteLine(line);
        }
        catch
        {
        }
    }
}
