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

public sealed record WMColorPreviewLook(
    string CacheKey,
    int LutSize,
    float[] LutValues,
    WMColorPreviewParameters Automatic,
    int PipelineVersion = WMColorPipelineVersion.Current)
{
    public static WMColorPreviewLook Identity { get; } = new(
        "identity",
        2,
        WMColorLut3D.Identity(2).Values,
        WMColorPreviewParameters.From(new WMColorGradeSettings()));

    public static WMColorPreviewLook From(WMGeneratedColorLook look) => new(
        look.CacheKey,
        look.ResidualLut.Size,
        look.ResidualLut.Values,
        WMColorPreviewParameters.From(look.BaseGrade));
}

public sealed record WMColorPreviewParameters(
    float[] Grade,
    float[] MasterCurve,
    float[] RedCurve,
    float[] GreenCurve,
    float[] BlueCurve,
    float[] Hsl)
{
    public static WMColorPreviewParameters From(WMColorGradeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new WMColorPreviewParameters(
            [
                settings.Exposure,
                settings.Contrast,
                settings.Highlights,
                settings.Shadows,
                settings.Whites,
                settings.Blacks,
                settings.Temperature,
                settings.Tint,
                settings.Vibrance,
                settings.Saturation
            ],
            SampleCurve(settings.MasterCurve),
            SampleCurve(settings.RedCurve),
            SampleCurve(settings.GreenCurve),
            SampleCurve(settings.BlueCurve),
            Enum.GetValues<WMHslBand>()
                .SelectMany(band =>
                {
                    var value = settings.Hsl.GetValueOrDefault(band) ?? new WMHslAdjustment();
                    return new[] { value.Hue, value.Saturation, value.Luminance };
                })
                .ToArray());
    }

    internal static float[] SampleCurve(IReadOnlyList<WMCurvePoint> points)
    {
        var normalized = WMCurvePoint.Normalize(points);
        var values = new float[4096];
        var segment = 0;
        for (var index = 0; index < values.Length; index++)
        {
            var x = index / (float)(values.Length - 1);
            while (segment < normalized.Count - 2 && x > normalized[segment + 1].X) segment++;
            var left = normalized[segment];
            var right = normalized[Math.Min(segment + 1, normalized.Count - 1)];
            var width = right.X - left.X;
            values[index] = Math.Clamp(width <= float.Epsilon
                ? right.Y
                : left.Y + (right.Y - left.Y) * ((x - left.X) / width), 0f, 1f);
        }
        return values;
    }
}

public sealed record WMColorPipelineProgram(
    int PipelineVersion,
    string ProgramFingerprint,
    WMColorPreviewLook Look,
    WMColorPreviewParameters Adjustments);

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
    IWMColorAnalysisService analysisService) : IWMColorPipelineCompiler
{
    private const int CacheLimit = 48;
    private const int CurveCacheLimit = 128;
    private readonly ConcurrentDictionary<string, Lazy<Task<WMColorPreviewLook>>> looks = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> lookOrder = new();
    private readonly ConcurrentDictionary<string, float[]> curveSamples = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> curveOrder = new();

    public async Task<WMColorPipelineProgram> CompileAsync(
        WMImageArtifact target,
        WMColorRecipe recipe,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(recipe);
        token.ThrowIfCancellationRequested();
        var normalized = CloneRecipe(recipe);
        normalized.UpgradeToCurrentSchema();
        var look = await GetLookAsync(target, normalized, token).ConfigureAwait(false);
        var adjustments = BuildParameters(normalized.Grade);
        var material = JsonSerializer.Serialize(new
        {
            Pipeline = normalized.PipelineVersion,
            Look = look.CacheKey,
            Grade = normalized.Grade
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return new WMColorPipelineProgram(
            normalized.PipelineVersion,
            fingerprint,
            look,
            adjustments);
    }

    private Task<WMColorPreviewLook> GetLookAsync(
        WMImageArtifact target,
        WMColorRecipe recipe,
        CancellationToken token)
    {
        if (!recipe.ReferenceMapping.Enabled || recipe.ReferenceProfile is null)
            return Task.FromResult(WMColorPreviewLook.Identity);

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
            return new Lazy<Task<WMColorPreviewLook>>(
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
                    return WMColorPreviewLook.From(generated);
                }, token),
                LazyThreadSafetyMode.ExecutionAndPublication);
        });
        return AwaitAndEvictFailureAsync(key, lazy);
    }

    private async Task<WMColorPreviewLook> AwaitAndEvictFailureAsync(
        string key,
        Lazy<Task<WMColorPreviewLook>> value)
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

    private WMColorPreviewParameters BuildParameters(WMColorGradeSettings settings) => new(
        [
            settings.Exposure,
            settings.Contrast,
            settings.Highlights,
            settings.Shadows,
            settings.Whites,
            settings.Blacks,
            settings.Temperature,
            settings.Tint,
            settings.Vibrance,
            settings.Saturation
        ],
        GetCurveSamples(settings.MasterCurve),
        GetCurveSamples(settings.RedCurve),
        GetCurveSamples(settings.GreenCurve),
        GetCurveSamples(settings.BlueCurve),
        Enum.GetValues<WMHslBand>()
            .SelectMany(band =>
            {
                var value = settings.Hsl.GetValueOrDefault(band) ?? new WMHslAdjustment();
                return new[] { value.Hue, value.Saturation, value.Luminance };
            })
            .ToArray());

    private float[] GetCurveSamples(IReadOnlyList<WMCurvePoint> points)
    {
        var normalized = WMCurvePoint.Normalize(points);
        var material = string.Join('|', normalized.Select(point => $"{point.X:R},{point.Y:R}"));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        if (curveSamples.TryGetValue(key, out var cached)) return cached;
        var samples = WMColorPreviewParameters.SampleCurve(normalized);
        if (curveSamples.TryAdd(key, samples))
        {
            curveOrder.Enqueue(key);
            while (curveSamples.Count > CurveCacheLimit && curveOrder.TryDequeue(out var expired))
                curveSamples.TryRemove(expired, out _);
            return samples;
        }
        return curveSamples[key];
    }

    private static WMColorRecipe CloneRecipe(WMColorRecipe recipe) =>
        WMColorRecipeSnapshot.Copy(recipe)!;
}

public sealed record WMColorPreviewValidationRequest(
    int Width,
    int Height,
    byte[] SourceRgba,
    byte[] ExpectedRgba,
    WMColorPreviewLook Look,
    WMColorPreviewParameters Adjustments,
    int PipelineVersion = WMColorPipelineVersion.Current);
