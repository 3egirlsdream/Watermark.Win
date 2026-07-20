using SkiaSharp;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMCropGeometryTests
{
    [Theory]
    [InlineData(WMCropAspectPreset.Original, 1.5)]
    [InlineData(WMCropAspectPreset.Square, 1.0)]
    [InlineData(WMCropAspectPreset.FourThree, 4d / 3)]
    [InlineData(WMCropAspectPreset.ThreeTwo, 1.5)]
    [InlineData(WMCropAspectPreset.SixteenNine, 16d / 9)]
    public void AspectPreset_ProducesRequestedPixelRatio(
        WMCropAspectPreset preset,
        double expected)
    {
        var settings = WMCropPlanner.SelectAspect(
            WMCropSettings.Identity with { AspectPreset = WMCropAspectPreset.Free },
            preset,
            1200,
            800);
        var plan = WMCropPlanner.CreatePlan(settings, 1200, 800);

        Assert.Equal(expected, plan.OutputWidth / (double)plan.OutputHeight, 2);
    }

    [Fact]
    public void SelectingNonSquareAspectAgain_TogglesPortraitOrientation()
    {
        var landscape = WMCropPlanner.SelectAspect(
            WMCropSettings.Identity,
            WMCropAspectPreset.SixteenNine,
            1600,
            1200);
        var portrait = WMCropPlanner.SelectAspect(
            landscape,
            WMCropAspectPreset.SixteenNine,
            1600,
            1200);
        var plan = WMCropPlanner.CreatePlan(portrait, 1600, 1200);

        Assert.True(portrait.AspectPortrait);
        Assert.True(plan.OutputHeight > plan.OutputWidth);
        Assert.Equal(9d / 16, plan.OutputWidth / (double)plan.OutputHeight, 2);
    }

    [Fact]
    public void FourClockwiseRotations_ReturnToOriginalGeometry()
    {
        var current = WMCropSettings.Identity;
        for (var index = 0; index < 4; index++)
            current = WMCropPlanner.RotateClockwise(current, 1200, 800);

        Assert.True(WMCropPlanner.IsIdentity(current));
        Assert.Equal(0, current.QuarterTurns);
        Assert.Equal(1, current.VisibleWidth, 6);
        Assert.Equal(1, current.VisibleHeight, 6);
    }

    [Fact]
    public void StraightenAndMinimumCrop_AreClampedWithoutTransparentPixels()
    {
        var settings = WMCropPlanner.Normalize(new WMCropSettings
        {
            CenterX = -2,
            CenterY = 3,
            VisibleWidth = .001,
            VisibleHeight = .001,
            StraightenDegrees = 80,
            AspectPreset = WMCropAspectPreset.Free
        }, 120, 80);
        Assert.InRange(settings.StraightenDegrees, -45, 45);
        Assert.True(settings.VisibleWidth >= 44d / 120);
        Assert.True(settings.VisibleHeight >= 44d / 80);

        using var source = new SKBitmap(120, 80, SKColorType.Bgra8888, SKAlphaType.Premul);
        source.Erase(new SKColor(40, 100, 180, 255));
        using var output = WMCropProcessor.Apply(source, WMCropSettings.Identity with
        {
            StraightenDegrees = 45,
            AspectPreset = WMCropAspectPreset.Free
        });

        for (var y = 0; y < output.Height; y++)
        for (var x = 0; x < output.Width; x++)
            Assert.Equal(255, output.GetPixel(x, y).Alpha);
    }

    [Fact]
    public void PreviewMaximumEdge_IsFusedIntoCropOutputPlan()
    {
        var plan = WMCropPlanner.CreatePlan(new WMCropSettings
        {
            VisibleWidth = .8,
            VisibleHeight = .6,
            AspectPreset = WMCropAspectPreset.Free
        }, 6000, 4000, 1200);

        Assert.Equal(1200, Math.Max(plan.OutputWidth, plan.OutputHeight));
        Assert.Equal(600, plan.OutputHeight);
    }
}
