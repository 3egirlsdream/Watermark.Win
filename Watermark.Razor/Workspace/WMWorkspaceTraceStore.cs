#nullable enable

using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed class WMWorkspaceTraceStore : IWMWorkspaceTraceStore
{
    private const long MaximumTraceBytes = 2L * 1024 * 1024;
    private const long MaximumLogBytes = 4L * 1024 * 1024;
    private const int MaximumReports = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex WindowsPathPattern = new(
        @"(?<![A-Za-z0-9])[A-Za-z]:\\(?:[^\s\""'<>|]+\\?)+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnixPathPattern = new(
        @"(?<![A-Za-z0-9])/(?:[^\s\""'<>/]+/)+[^\s\""'<>]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SecretPattern = new(
        @"(?i)\b(bearer|token|secret|password|authorization|api[-_]?key|template[-_]?key)\b\s*[:=]?\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AccountPattern = new(
        @"(?i)\b(user[-_]?id|user[-_]?name|account[-_]?id|account[-_]?name)\b\s*[:=]\s*[^\s,;]+|账号\s*[:：=]\s*[^\s,，;；]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string directory;
    private readonly string tracePath;
    private readonly string logPath;

    public WMWorkspaceTraceStore()
        : this(Path.Combine(Global.AppPath.BasePath, "Cache", "diagnostics"))
    {
    }

    public WMWorkspaceTraceStore(string directory)
    {
        this.directory = directory;
        tracePath = Path.Combine(directory, "workspace-trace.jsonl");
        logPath = Path.Combine(directory, "application-log.jsonl");
    }

    public async Task RecordLogAsync(
        WMDiagnosticLogEvent logEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var safe = Sanitize(logEvent);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(logPath) && new FileInfo(logPath).Length >= MaximumLogBytes)
                await TrimAsync(logPath, cancellationToken).ConfigureAwait(false);
            await File.AppendAllTextAsync(
                logPath,
                JsonSerializer.Serialize(safe, JsonOptions) + Environment.NewLine,
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RecordAsync(
        WMWorkspaceTraceEvent traceEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(traceEvent);
        var safe = Sanitize(traceEvent);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(tracePath) && new FileInfo(tracePath).Length >= MaximumTraceBytes)
                await TrimAsync(tracePath, cancellationToken).ConfigureAwait(false);
            await File.AppendAllTextAsync(
                tracePath,
                JsonSerializer.Serialize(safe, JsonOptions) + Environment.NewLine,
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<WMWorkspaceTraceEvent>> ReadLatestAsync(
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(tracePath)) return [];
            var lines = await File.ReadAllLinesAsync(tracePath, cancellationToken).ConfigureAwait(false);
            return lines.Reverse()
                .Select(TryDeserialize)
                .Where(item => item is not null)
                .Take(Math.Clamp(take, 1, 500))
                .Cast<WMWorkspaceTraceEvent>()
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<WMDiagnosticLogEvent>> ReadLatestLogsAsync(
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(logPath)) return [];
            var lines = await File.ReadAllLinesAsync(logPath, cancellationToken).ConfigureAwait(false);
            return lines.Reverse()
                .Select(TryDeserializeLog)
                .Where(item => item is not null)
                .Take(Math.Clamp(take, 1, 1000))
                .Cast<WMDiagnosticLogEvent>()
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> CreateReportAsync(
        WMImagingDiagnosticSnapshot? imaging = null,
        CancellationToken cancellationToken = default)
    {
        var latest = await ReadLatestAsync(500, cancellationToken).ConfigureAwait(false);
        var logs = await ReadLatestLogsAsync(1000, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(directory);
        var generatedAtUtc = DateTime.UtcNow;
        var reportId = Guid.NewGuid().ToString("N");
        var reportPath = Path.Combine(directory, $"litograph-diagnostics-{generatedAtUtc:yyyyMMdd-HHmmss}.json");
        var temporary = reportPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 2,
                    reportId,
                    generatedAtUtc,
                    runtime = new
                    {
                        appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                                     ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                        os = RuntimeInformation.OSDescription,
                        framework = RuntimeInformation.FrameworkDescription,
                        processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                        osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                        processorCount = Environment.ProcessorCount,
                        is64BitProcess = Environment.Is64BitProcess,
                        workingSetBytes = Environment.WorkingSet,
                        utcOffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(generatedAtUtc).TotalMinutes
                    },
                    imaging,
                    summary = new
                    {
                        traceEventCount = latest.Count,
                        logEventCount = logs.Count,
                        errorCount = logs.Count(item => item.Level >= WMDiagnosticLogLevel.Error),
                        warningCount = logs.Count(item => item.Level == WMDiagnosticLogLevel.Warning),
                        canceledCount = latest.Count(item => item.Canceled),
                        cacheHitCount = latest.Count(item => item.CacheHit)
                    },
                    logs,
                    workspaceEvents = latest
                }, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }),
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, reportPath, true);
            CleanupReports(reportPath);
            return reportPath;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TryDelete(tracePath);
            TryDelete(logPath);
            if (Directory.Exists(directory))
            {
                foreach (var path in Directory.EnumerateFiles(directory, "litograph-diagnostics-*.json"))
                    TryDelete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public static string SessionKey(string sessionId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId ?? string.Empty));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private async Task TrimAsync(string path, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        var keep = lines.Skip(lines.Length / 2).ToArray();
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllLinesAsync(temporary, keep, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static WMWorkspaceTraceEvent Sanitize(WMWorkspaceTraceEvent value) => value with
    {
        SessionKey = NormalizeSessionKey(value.SessionKey),
        Fingerprint = NormalizeIdentifier(value.Fingerprint, 96),
        JobId = NormalizeIdentifier(value.JobId, 64),
        EventName = NormalizeToken(value.EventName, 64) ?? "unknown",
        ErrorCode = NormalizeErrorCode(value.ErrorCode)
    };

    private static WMDiagnosticLogEvent Sanitize(WMDiagnosticLogEvent value) => value with
    {
        Category = NormalizeToken(value.Category, 96) ?? "Application",
        EventName = NormalizeToken(value.EventName, 96) ?? "event",
        Message = SanitizeText(value.Message, 1000),
        ExceptionType = NormalizeToken(value.ExceptionType, 128),
        ErrorCode = NormalizeErrorCode(value.ErrorCode),
        SessionKey = string.IsNullOrWhiteSpace(value.SessionKey)
            ? null
            : NormalizeSessionKey(value.SessionKey),
        StackTrace = SanitizeText(value.StackTrace, 4000),
        Properties = value.Properties?
            .Take(24)
            .Select(item => new KeyValuePair<string, string>(
                NormalizeToken(item.Key, 64) ?? "property",
                SanitizeText(item.Value, 256) ?? string.Empty))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal)
    };

    private static string? NormalizeToken(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safe = new string(value.Where(character => char.IsLetterOrDigit(character)
                                                       || character is '-' or '_' or '.' or ':').ToArray());
        return safe.Length <= maximumLength ? safe : safe[..maximumLength];
    }

    private static string NormalizeSessionKey(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && value.Length == 16
            && value.All(Uri.IsHexDigit))
            return value.ToUpperInvariant();
        return SessionKey(value ?? string.Empty);
    }

    private static string? NormalizeIdentifier(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = NormalizeToken(value, maximumLength);
        return string.Equals(normalized, value, StringComparison.Ordinal)
            ? normalized
            : SessionKey(value);
    }

    private static string? NormalizeErrorCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = NormalizeToken(value, 96);
        return string.Equals(normalized, value, StringComparison.Ordinal)
            ? normalized
            : "redacted";
    }

    private static string? SanitizeText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safe = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        safe = WindowsPathPattern.Replace(safe, "[path]");
        safe = UnixPathPattern.Replace(safe, "[path]");
        safe = EmailPattern.Replace(safe, "[account]");
        safe = AccountPattern.Replace(safe, "account=[redacted]");
        safe = SecretPattern.Replace(safe, "$1=[redacted]");
        return safe.Length <= maximumLength ? safe : safe[..maximumLength];
    }

    private static WMWorkspaceTraceEvent? TryDeserialize(string line)
    {
        try { return JsonSerializer.Deserialize<WMWorkspaceTraceEvent>(line, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static WMDiagnosticLogEvent? TryDeserializeLog(string line)
    {
        try { return JsonSerializer.Deserialize<WMDiagnosticLogEvent>(line, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private void CleanupReports(string currentReport)
    {
        try
        {
            var stale = Directory.EnumerateFiles(directory, "litograph-diagnostics-*.json")
                .Where(path => !string.Equals(path, currentReport, StringComparison.Ordinal))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(MaximumReports - 1)
                .ToArray();
            foreach (var path in stale) TryDelete(path);
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
