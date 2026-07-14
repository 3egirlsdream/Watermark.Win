#nullable enable
using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac;

public sealed record MacColorPreviewCapability(
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

public sealed record MacColorPreviewValidationResult(
    bool Passed,
    double AverageDeltaE,
    double MaximumDeltaE,
    int MaximumChannelError,
    double ChannelPassRate,
    string? Reason = null);

public sealed record MacColorPreviewLook(
    string CacheKey,
    int LutSize,
    float[] LutValues,
    MacColorPreviewParameters Automatic,
    int PipelineVersion = WMColorPipelineVersion.Current)
{
    public static MacColorPreviewLook Identity { get; } = new(
        "identity",
        2,
        WMColorLut3D.Identity(2).Values,
        MacColorPreviewParameters.From(new WMColorGradeSettings()),
        WMColorPipelineVersion.Current);

    public static MacColorPreviewLook From(WMGeneratedColorLook look) => new(
        look.CacheKey,
        look.ResidualLut.Size,
        look.ResidualLut.Values,
        MacColorPreviewParameters.From(look.BaseGrade),
        WMColorPipelineVersion.Current);
}

public sealed record MacColorPreviewValidationRequest(
    int Width,
    int Height,
    byte[] SourceRgba,
    byte[] ExpectedRgba,
    MacColorPreviewLook Look,
    MacColorPreviewParameters Adjustments,
    int PipelineVersion = WMColorPipelineVersion.Current);

public sealed record MacColorPreviewParameters(
    float[] Grade,
    float[] MasterCurve,
    float[] RedCurve,
    float[] GreenCurve,
    float[] BlueCurve,
    float[] Hsl)
{
    public static MacColorPreviewParameters From(WMColorGradeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        return new MacColorPreviewParameters(
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
            BuildCurve(settings.MasterCurve),
            BuildCurve(settings.RedCurve),
            BuildCurve(settings.GreenCurve),
            BuildCurve(settings.BlueCurve),
            Enum.GetValues<WMHslBand>()
                .SelectMany(band =>
                {
                    var value = settings.Hsl.GetValueOrDefault(band) ?? new WMHslAdjustment();
                    return new[] { value.Hue, value.Saturation, value.Luminance };
                })
                .ToArray());
    }

    public static MacColorPreviewParameters FromGradeAndHsl(
        WMColorGradeSettings settings,
        MacColorPreviewParameters previous)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(previous);
        return new MacColorPreviewParameters(
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
            previous.MasterCurve,
            previous.RedCurve,
            previous.GreenCurve,
            previous.BlueCurve,
            Enum.GetValues<WMHslBand>()
                .SelectMany(band =>
                {
                    var value = settings.Hsl.GetValueOrDefault(band) ?? new WMHslAdjustment();
                    return new[] { value.Hue, value.Saturation, value.Luminance };
                })
                .ToArray());
    }

    public static MacColorPreviewParameters FromCurves(
        WMColorGradeSettings settings,
        MacColorPreviewParameters previous)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(previous);
        return new MacColorPreviewParameters(
            previous.Grade,
            ReuseUnchangedCurve(previous.MasterCurve, BuildCurve(settings.MasterCurve)),
            ReuseUnchangedCurve(previous.RedCurve, BuildCurve(settings.RedCurve)),
            ReuseUnchangedCurve(previous.GreenCurve, BuildCurve(settings.GreenCurve)),
            ReuseUnchangedCurve(previous.BlueCurve, BuildCurve(settings.BlueCurve)),
            previous.Hsl);
    }

    private static float[] ReuseUnchangedCurve(float[] previous, float[] current) =>
        previous.AsSpan().SequenceEqual(current) ? previous : current;

    private static float[] BuildCurve(IReadOnlyList<WMCurvePoint> points)
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
