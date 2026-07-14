using Watermark.Razor.Components.Mac;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacColorPreviewTypesTests
{
    [Fact]
    public void Parameters_PreserveCpuGradeOrderAndCurveSampling()
    {
        var settings = new WMColorGradeSettings
        {
            Exposure = 1.25f,
            Contrast = 20,
            Highlights = 30,
            Shadows = -40,
            Whites = 50,
            Blacks = -60,
            Temperature = 70,
            Tint = -80,
            Vibrance = 90,
            Saturation = -10,
            MasterCurve =
            [
                new WMCurvePoint { X = 0, Y = 0 },
                new WMCurvePoint { X = 0.5f, Y = 0.75f },
                new WMCurvePoint { X = 1, Y = 1 }
            ]
        };
        settings.Hsl[WMHslBand.Blue] = new WMHslAdjustment { Hue = 11, Saturation = 22, Luminance = 33 };

        var result = MacColorPreviewParameters.From(settings);

        Assert.Equal([1.25f, 20, 30, -40, 50, -60, 70, -80, 90, -10], result.Grade);
        Assert.Equal(4096, result.MasterCurve.Length);
        Assert.InRange(result.MasterCurve[2048], 0.749f, 0.753f);
        Assert.Equal(24, result.Hsl.Length);
        var blueOffset = (int)WMHslBand.Blue * 3;
        Assert.Equal([11, 22, 33], result.Hsl.Skip(blueOffset).Take(3));
    }

    [Fact]
    public void IdentityLook_UsesSmallValidLutAndNeutralAdjustments()
    {
        var look = MacColorPreviewLook.Identity;

        Assert.Equal(2, look.LutSize);
        Assert.Equal(WMColorPipelineVersion.Current, look.PipelineVersion);
        Assert.Equal(2 * 2 * 2 * 3, look.LutValues.Length);
        Assert.All(look.Automatic.Grade, value => Assert.Equal(0, value));
        Assert.Equal(0, look.Automatic.MasterCurve[0]);
        Assert.Equal(1, look.Automatic.MasterCurve[^1]);
    }

    [Fact]
    public void GradeAndHslFastPath_ReusesCurveSamples()
    {
        var previous = MacColorPreviewParameters.From(new WMColorGradeSettings());
        var settings = new WMColorGradeSettings { Exposure = 1.5f, Saturation = 12 };
        settings.Hsl[WMHslBand.Green] = new WMHslAdjustment { Hue = 7 };

        var result = MacColorPreviewParameters.FromGradeAndHsl(settings, previous);

        Assert.Equal(1.5f, result.Grade[0]);
        Assert.Equal(12, result.Grade[9]);
        Assert.Same(previous.MasterCurve, result.MasterCurve);
        Assert.Same(previous.RedCurve, result.RedCurve);
        Assert.Same(previous.GreenCurve, result.GreenCurve);
        Assert.Same(previous.BlueCurve, result.BlueCurve);
        Assert.Equal(7, result.Hsl[(int)WMHslBand.Green * 3]);
    }

    [Fact]
    public void CurveFastPath_OnlyRebuildsCurveSamples()
    {
        var previous = MacColorPreviewParameters.From(new WMColorGradeSettings
        {
            Exposure = 1.25f
        });
        var settings = new WMColorGradeSettings
        {
            MasterCurve =
            [
                new WMCurvePoint { X = 0, Y = 0 },
                new WMCurvePoint { X = 0.5f, Y = 0.8f },
                new WMCurvePoint { X = 1, Y = 1 }
            ]
        };

        var result = MacColorPreviewParameters.FromCurves(settings, previous);

        Assert.Same(previous.Grade, result.Grade);
        Assert.Same(previous.Hsl, result.Hsl);
        Assert.NotSame(previous.MasterCurve, result.MasterCurve);
        Assert.Same(previous.RedCurve, result.RedCurve);
        Assert.Same(previous.GreenCurve, result.GreenCurve);
        Assert.Same(previous.BlueCurve, result.BlueCurve);
        Assert.InRange(result.MasterCurve[2048], 0.799f, 0.803f);
    }
}
