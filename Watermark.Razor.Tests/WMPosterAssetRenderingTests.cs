using SkiaSharp;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMPosterAssetRenderingTests
{
    [Fact]
    public void Cover_CropsTheSourceWithoutAddingAnotherResizeStage()
    {
        var geometry = WMPosterAssetRendering.CalculateGeometry(
            400,
            200,
            new SKRect(0, 0, 100, 100),
            WMPosterAssetFit.Cover,
            WMCropSettings.Identity);

        Assert.Equal(new SKRect(0, 0, 400, 200), geometry.Source);
        Assert.Equal(new SKRect(-50, 0, 150, 100), geometry.Destination);
    }

    [Fact]
    public void Contain_KeepsTheWholeCropAndCentersLetterboxing()
    {
        var geometry = WMPosterAssetRendering.CalculateGeometry(
            400,
            200,
            new SKRect(0, 0, 100, 100),
            WMPosterAssetFit.Contain,
            WMCropSettings.Identity);

        Assert.Equal(new SKRect(0, 0, 400, 200), geometry.Source);
        Assert.Equal(0, geometry.Destination.Left, 3);
        Assert.Equal(25, geometry.Destination.Top, 3);
        Assert.Equal(100, geometry.Destination.Right, 3);
        Assert.Equal(75, geometry.Destination.Bottom, 3);
    }

    [Fact]
    public void ExplicitRatio_IsAppliedBeforeSlotFit()
    {
        var geometry = WMPosterAssetRendering.CalculateGeometry(
            400,
            200,
            new SKRect(0, 0, 100, 100),
            WMPosterAssetFit.Contain,
            WMCropSettings.Identity with
            {
                CenterX = 0.5,
                CenterY = 0.5,
                VisibleWidth = 0.5,
                VisibleHeight = 1,
                AspectPreset = WMCropAspectPreset.Free
            });

        Assert.Equal(new SKRect(0, 0, 400, 200), geometry.Source);
        Assert.Equal(new SKRect(0, 0, 100, 100), geometry.Destination);
    }

    [Fact]
    public void Draw_ReplaysTheSharedCropPlannerMatrixWithoutAnIntermediateBitmap()
    {
        using var source = new SKBitmap(120, 80);
        using (var sourceCanvas = new SKCanvas(source))
        {
            sourceCanvas.Clear(SKColors.Transparent);
            sourceCanvas.DrawRect(new SKRect(0, 0, 60, 40), new SKPaint { Color = SKColors.Red });
            sourceCanvas.DrawRect(new SKRect(60, 0, 120, 40), new SKPaint { Color = SKColors.Green });
            sourceCanvas.DrawRect(new SKRect(0, 40, 60, 80), new SKPaint { Color = SKColors.Blue });
            sourceCanvas.DrawRect(new SKRect(60, 40, 120, 80), new SKPaint { Color = SKColors.Yellow });
        }

        var crop = WMCropPlanner.Normalize(
            WMCropSettings.Identity with
            {
                CenterX = 0.48,
                CenterY = 0.53,
                VisibleWidth = 0.72,
                VisibleHeight = 0.68,
                QuarterTurns = 1,
                FlipHorizontal = true,
                StraightenDegrees = 4,
                AspectPreset = WMCropAspectPreset.Free
            },
            source.Width,
            source.Height);
        using var expected = WMCropProcessor.Apply(source, crop);
        using var actual = new SKBitmap(expected.Width, expected.Height);
        using var actualCanvas = new SKCanvas(actual);
        actualCanvas.Clear(SKColors.Transparent);
        var slot = new WMPosterAssetSlot { Fit = WMPosterAssetFit.Fill };
        slot.SetCropSettings(crop, source.Width, source.Height);
        Assert.Equal(crop, slot.GetCropSettings(source.Width, source.Height));

        WMPosterAssetRendering.Draw(
            actualCanvas,
            source,
            new SKRect(0, 0, actual.Width, actual.Height),
            slot);
        actualCanvas.Flush();

        // The direct matrix intentionally avoids the intermediate bitmap. Its
        // outer antialias fringe may differ by one pixel, while interior crop
        // coordinates must remain identical to WMCropProcessor.
        long totalChannelDelta = 0;
        var sampledChannels = 0;
        for (var y = 5; y < actual.Height - 5; y += 2)
        for (var x = 5; x < actual.Width - 5; x += 2)
        {
            var expectedPixel = expected.GetPixel(x, y);
            var actualPixel = actual.GetPixel(x, y);
            totalChannelDelta += Math.Abs(expectedPixel.Red - actualPixel.Red);
            totalChannelDelta += Math.Abs(expectedPixel.Green - actualPixel.Green);
            totalChannelDelta += Math.Abs(expectedPixel.Blue - actualPixel.Blue);
            totalChannelDelta += Math.Abs(expectedPixel.Alpha - actualPixel.Alpha);
            sampledChannels += 4;
        }
        Assert.True(
            totalChannelDelta / (double)sampledChannels < 8,
            $"Average channel delta was {totalChannelDelta / (double)sampledChannels:0.###}.");
    }

    [Fact]
    public void RendererOwnsNoDecodeOrExplicitBitmapResize()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Watermark.Shared",
            "Models",
            "WMPosterAssetRendering.cs"));

        Assert.DoesNotContain("SKBitmap.Decode", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Resize(", source, StringComparison.Ordinal);
        Assert.Equal(
            1,
            source.Split("canvas.DrawBitmap(", StringSplitOptions.None).Length - 1);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Watermark.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Watermark.sln not found.");
    }
}
