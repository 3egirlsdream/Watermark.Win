#nullable enable

using Microsoft.Extensions.Logging;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Persists application warnings/errors and Watermark information events into
/// the same bounded, sanitized diagnostic store used by workspace traces.
/// </summary>
public sealed class WMDiagnosticLoggerProvider(IWMWorkspaceTraceStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(store, categoryName);

    public void Dispose() { }

    private sealed class DiagnosticLogger(
        IWMWorkspaceTraceStore store,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Warning
            || (category.StartsWith("Watermark", StringComparison.Ordinal)
                && logLevel >= LogLevel.Information);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string? message;
            try { message = formatter(state, exception); }
            catch { message = exception?.Message; }

            Observe(store.RecordLogAsync(new WMDiagnosticLogEvent(
                DateTime.UtcNow,
                Convert(logLevel),
                category,
                string.IsNullOrWhiteSpace(eventId.Name) ? $"log-{eventId.Id}" : eventId.Name,
                message,
                exception?.GetType().FullName,
                exception is null ? null : $"0x{exception.HResult:X8}",
                StackTrace: exception?.StackTrace)));
        }
    }

    internal static void Observe(Task task) => _ = task.ContinueWith(
        static completed => _ = completed.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private static WMDiagnosticLogLevel Convert(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => WMDiagnosticLogLevel.Debug,
        LogLevel.Information => WMDiagnosticLogLevel.Information,
        LogLevel.Warning => WMDiagnosticLogLevel.Warning,
        LogLevel.Error => WMDiagnosticLogLevel.Error,
        LogLevel.Critical => WMDiagnosticLogLevel.Critical,
        _ => WMDiagnosticLogLevel.Information
    };
}

public static class WMDiagnosticUnhandledExceptionRegistration
{
    private static int registered;

    public static void Register(IWMWorkspaceTraceStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (Interlocked.Exchange(ref registered, 1) != 0) return;

        WMDiagnosticLoggerProvider.Observe(store.RecordLogAsync(new WMDiagnosticLogEvent(
            DateTime.UtcNow,
            WMDiagnosticLogLevel.Information,
            "Application.Runtime",
            "application-started",
            "应用诊断日志已启动。",
            Properties: new Dictionary<string, string>
            {
                ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                ["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            })));

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            Record(store, "unhandled-exception", exception, args.IsTerminating, args.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Record(store, "unobserved-task-exception", args.Exception, false, false);
            args.SetObserved();
        };
    }

    private static void Record(
        IWMWorkspaceTraceStore store,
        string eventName,
        Exception? exception,
        bool terminating,
        bool flushBeforeExit)
    {
        var write = store.RecordLogAsync(new WMDiagnosticLogEvent(
            DateTime.UtcNow,
            terminating ? WMDiagnosticLogLevel.Critical : WMDiagnosticLogLevel.Error,
            "Application.Runtime",
            eventName,
            exception?.Message ?? "发生未处理的运行时异常。",
            exception?.GetType().FullName,
            exception is null ? null : $"0x{exception.HResult:X8}",
            Properties: new Dictionary<string, string>
            {
                ["terminating"] = terminating.ToString()
            },
            StackTrace: exception?.StackTrace));
        if (!flushBeforeExit)
        {
            WMDiagnosticLoggerProvider.Observe(write);
            return;
        }

        try { write.GetAwaiter().GetResult(); }
        catch { }
    }
}
