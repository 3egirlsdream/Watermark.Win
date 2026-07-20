#nullable enable

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public enum WMInteractivePreviewBackend
{
    WebGl2,
    CpuSkia
}

public sealed record WMColorPreviewCapability(
    bool Supported,
    string? Reason = null,
    int Max3DTextureSize = 0,
    int PipelineVersion = 0,
    bool Validated = false,
    string? Renderer = null,
    string? EnvironmentKey = null,
    double? AverageDeltaE = null,
    double? MaximumDeltaE = null,
    int? MaximumChannelError = null);

public sealed record WMColorPreviewValidationResult(
    bool Passed,
    double AverageDeltaE,
    double MaximumDeltaE,
    int MaximumChannelError,
    double ChannelPassRate,
    string? Reason = null);

public sealed record WMColorPreviewPerformanceSample(
    string Stage,
    double DurationMilliseconds,
    string? Detail = null);

public sealed record WMColorPipelineProgram(
    int PipelineVersion,
    string ProgramFingerprint,
    WMColorGpuProgram GpuProgram);

public interface IWMColorPipelineCompiler
{
    Task<WMColorPipelineProgram> CompileAsync(
        WMImageArtifact target,
        WMColorRecipe recipe,
        CancellationToken token);
}

public sealed record WMWorkspacePreviewPresentation(
    long Version,
    string BaseFingerprint,
    string? BaseUrl,
    string? StableUrl,
    WMColorPipelineProgram? ColorProgram,
    WMInteractivePreviewBackend Backend,
    bool IsSettled,
    string? FallbackReason)
{
    public static WMWorkspacePreviewPresentation Empty { get; } = new(
        0, string.Empty, null, null, null, WMInteractivePreviewBackend.CpuSkia, true, null);
}

public sealed class WMColorPipelineCompiler(
    IWMColorLookMapper lookMapper,
    IWMColorAnalysisService analysisService,
    IWMColorEngine colorEngine,
    IWMWorkspacePerformanceCounters? metrics = null) : IWMColorPipelineCompiler, IDisposable
{
    private const int CacheLimit = 48;
    private const int ProcessorCacheLimit = 16;
    private readonly ConcurrentDictionary<string, Lazy<Task<WMGeneratedColorLook?>>> looks = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> lookOrder = new();
    private readonly Dictionary<string, ProcessorEntry> processors = new(StringComparer.Ordinal);
    private readonly Queue<string> processorOrder = new();
    private readonly object processorGate = new();
    private bool disposed;

    public async Task<WMColorPipelineProgram> CompileAsync(
        WMImageArtifact target,
        WMColorRecipe recipe,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(recipe);
        token.ThrowIfCancellationRequested();
        if (!colorEngine.Capability.IsAvailable)
            throw new PlatformNotSupportedException(colorEngine.Capability.Error);
        var normalized = CloneRecipe(recipe);
        normalized.UpgradeToCurrentSchema();
        var look = await GetLookAsync(target, normalized, token).ConfigureAwait(false);
        var staticKey = $"{normalized.PipelineVersion}|{look?.CacheKey ?? "identity"}|srgb-srgb";
        var entry = AcquireProcessor(staticKey, () =>
        {
            using var measurement = metrics?.Measure(WMWorkspaceMetricStage.ProcessorCompile);
            return colorEngine.CreateProcessor(
                new WMColorPipelineDefinition(look?.BaseGrade, look?.ResidualLut),
                new WMColorDynamicState(normalized.Grade));
        });
        WMColorGpuProgram gpuProgram;
        try
        {
            await entry.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using (metrics?.Measure(WMWorkspaceMetricStage.DynamicUpdate))
                    colorEngine.Update(entry.Processor, new WMColorDynamicState(normalized.Grade));
                using (metrics?.Measure(WMWorkspaceMetricStage.GpuUpload))
                    gpuProgram = colorEngine.CreateGpuProgram(entry.Processor);
            }
            finally { entry.Gate.Release(); }
        }
        finally { ReleaseProcessor(entry); }
        var material = JsonSerializer.Serialize(new
        {
            Pipeline = normalized.PipelineVersion,
            Look = look?.CacheKey ?? "identity",
            Shader = gpuProgram.ShaderCacheId,
            Grade = normalized.Grade
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return new WMColorPipelineProgram(
            normalized.PipelineVersion,
            fingerprint,
            gpuProgram);
    }

    private Task<WMGeneratedColorLook?> GetLookAsync(
        WMImageArtifact target,
        WMColorRecipe recipe,
        CancellationToken token)
    {
        if (!recipe.ReferenceMapping.Enabled || recipe.ReferenceProfile is null)
            return Task.FromResult<WMGeneratedColorLook?>(null);

        var source = target.ContentHash ?? target.SourceFingerprint?.StableId ?? target.Id;
        var reference = recipe.ReferenceProfile.SourceHash;
        if (string.IsNullOrWhiteSpace(reference))
            reference = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(recipe.ReferenceProfile))));
        var settings = JsonSerializer.Serialize(recipe.ReferenceMapping);
        var key = $"{source}|{reference}|{settings}|{recipe.MappingVersion}|{recipe.PipelineVersion}";
        var lazy = looks.GetOrAdd(key, _ =>
        {
            lookOrder.Enqueue(key);
            TrimCache();
            return new Lazy<Task<WMGeneratedColorLook?>>(
                () => Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var path = target.PreviewPath is { Length: > 0 } preview && File.Exists(preview)
                        ? preview
                        : target.FilePath;
                    var targetProfile = analysisService.Analyze(path, token);
                    var generated = lookMapper.Map(new WMColorLookMappingRequest(
                        recipe.ReferenceProfile,
                        targetProfile,
                        recipe.ReferenceMapping,
                        recipe.MappingVersion,
                        33), token);
                    return (WMGeneratedColorLook?)generated;
                }, token),
                LazyThreadSafetyMode.ExecutionAndPublication);
        });
        return AwaitAndEvictFailureAsync(key, lazy);
    }

    private async Task<WMGeneratedColorLook?> AwaitAndEvictFailureAsync(
        string key,
        Lazy<Task<WMGeneratedColorLook?>> value)
    {
        try { return await value.Value.ConfigureAwait(false); }
        catch
        {
            looks.TryRemove(key, out _);
            throw;
        }
    }

    private void TrimCache()
    {
        while (looks.Count >= CacheLimit && lookOrder.TryDequeue(out var expired))
            looks.TryRemove(expired, out _);
    }

    private ProcessorEntry AcquireProcessor(string key, Func<WMColorProcessorLease> create)
    {
        lock (processorGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!processors.TryGetValue(key, out var entry))
            {
                entry = new ProcessorEntry(key, create());
                processors.Add(key, entry);
                processorOrder.Enqueue(key);
            }
            entry.Users++;
            TrimProcessorCache(key);
            return entry;
        }
    }

    private void ReleaseProcessor(ProcessorEntry entry)
    {
        lock (processorGate) entry.Users = Math.Max(0, entry.Users - 1);
    }

    private void TrimProcessorCache(string retainedKey)
    {
        var attempts = processorOrder.Count;
        while (processors.Count > ProcessorCacheLimit && attempts-- > 0 && processorOrder.TryDequeue(out var key))
        {
            if (string.Equals(key, retainedKey, StringComparison.Ordinal)
                || !processors.TryGetValue(key, out var entry)
                || entry.Users > 0)
            {
                processorOrder.Enqueue(key);
                continue;
            }
            processors.Remove(key);
            entry.Dispose();
        }
    }

    public void Dispose()
    {
        lock (processorGate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in processors.Values) entry.Dispose();
            processors.Clear();
            processorOrder.Clear();
        }
    }

    private sealed class ProcessorEntry(string key, WMColorProcessorLease processor) : IDisposable
    {
        public string Key { get; } = key;
        public WMColorProcessorLease Processor { get; } = processor;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Users { get; set; }

        public void Dispose()
        {
            Processor.Dispose();
            Gate.Dispose();
        }
    }

    private static WMColorRecipe CloneRecipe(WMColorRecipe recipe) =>
        WMColorRecipeSnapshot.Copy(recipe)!;
}

public sealed record WMColorPreviewValidationRequest(
    int Width,
    int Height,
    byte[] SourceRgba,
    byte[] ExpectedRgba,
    WMColorGpuProgram Program,
    int PipelineVersion = WMColorPipelineVersion.Current);
