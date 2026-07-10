using Watermark.Razor.Components.Mac.Editor;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacCanvasTransformTests
{
    [Fact]
    public void Apply_ClampsInteractionAndSynchronizesContainerAngle()
    {
        var container = new WMContainer { ID = "CONTAINER" };

        MacCanvasTransform.Apply(container, new MacCanvasInteraction(
            container.ID,
            "resize",
            900,
            -900,
            40,
            0.001,
            195));

        Assert.Equal(500, container.Transform!.OffsetXPercent);
        Assert.Equal(-500, container.Transform.OffsetYPercent);
        Assert.Equal(20, container.Transform.ScaleX);
        Assert.Equal(0.05, container.Transform.ScaleY);
        Assert.Equal(-165, container.Transform.Rotation);
        Assert.Equal(-165, container.Angle);
    }

    [Fact]
    public void Apply_PreservesExistingValuesWhenPayloadIsNotFinite()
    {
        var text = new WMText
        {
            ID = "TEXT",
            Transform = new WMTransform
            {
                OffsetXPercent = 12,
                OffsetYPercent = -8,
                ScaleX = 1.5,
                ScaleY = 0.75,
                Rotation = 30
            }
        };

        MacCanvasTransform.Apply(text, new MacCanvasInteraction(
            text.ID,
            "drag",
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
            double.NaN,
            double.NaN));

        Assert.Equal(12, text.Transform.OffsetXPercent);
        Assert.Equal(-8, text.Transform.OffsetYPercent);
        Assert.Equal(1.5, text.Transform.ScaleX);
        Assert.Equal(0.75, text.Transform.ScaleY);
        Assert.Equal(30, text.Transform.Rotation);
    }

    [Fact]
    public void Apply_RejectsMismatchedControlIds()
    {
        var text = new WMText { ID = "TEXT" };

        Assert.Throws<ArgumentException>(() => MacCanvasTransform.Apply(text, new MacCanvasInteraction(
            "OTHER", "drag", 0, 0, 1, 1, 0)));
    }
}
