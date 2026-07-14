using Watermark.Razor.Components.Mac.Editor;
using Watermark.Shared.Enums;
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

    [Fact]
    public void ConstrainDrag_KeepsRotatedChildInsideParent()
    {
        var bounds = Bounds("TEXT", "PARENT", 20, 30, 40, 20, 100, 100);
        var interaction = new MacCanvasInteraction("TEXT", "drag", 100, -100, 1, 1, 90);

        var constrained = MacCanvasBoundary.ConstrainDrag(bounds, interaction);

        Assert.Equal(50, constrained.OffsetXPercent, 6);
        Assert.Equal(-20, constrained.OffsetYPercent, 6);
    }

    [Fact]
    public void ClampOffsets_CentersAxisWhenScaledChildIsLargerThanParent()
    {
        var bounds = Bounds("TEXT", "PARENT", 20, 30, 40, 20, 100, 100);

        var offsets = MacCanvasBoundary.ClampOffsets(bounds, 80, 0, 3, 1, 0);

        Assert.Equal(10, offsets.OffsetXPercent, 6);
        Assert.Equal(0, offsets.OffsetYPercent, 6);
    }

    [Fact]
    public void ConstrainDrag_DoesNotConstrainRootContainer()
    {
        var bounds = Bounds("ROOT", null, 20, 30, 40, 20, 100, 100);
        var interaction = new MacCanvasInteraction("ROOT", "drag", 175, -125, 1, 1, 0);

        Assert.Same(interaction, MacCanvasBoundary.ConstrainDrag(bounds, interaction));
    }

    [Fact]
    public void ConstrainTransform_ShrinksResizedChildAndKeepsItInsideParent()
    {
        var bounds = Bounds("LOGO", "PARENT", 60, 60, 40, 40, 100, 100);
        var interaction = new MacCanvasInteraction("LOGO", "resize", 20, 20, 3, 3, 0);

        var constrained = MacCanvasBoundary.ConstrainTransform(bounds, interaction);

        Assert.Equal(2.5, constrained.ScaleX, 6);
        Assert.Equal(2.5, constrained.ScaleY, 6);
        Assert.Equal(-30, constrained.OffsetXPercent, 6);
        Assert.Equal(-30, constrained.OffsetYPercent, 6);
    }

    [Fact]
    public void ConstrainTransform_AccountsForRotatedBounds()
    {
        var bounds = Bounds("LOGO", "PARENT", 30, 40, 40, 20, 100, 100);
        var interaction = new MacCanvasInteraction("LOGO", "rotate", 100, 100, 3, 3, 90);

        var constrained = MacCanvasBoundary.ConstrainTransform(bounds, interaction);

        Assert.Equal(2.5, constrained.ScaleX, 6);
        Assert.Equal(2.5, constrained.ScaleY, 6);
        Assert.Equal(25, constrained.OffsetXPercent, 6);
        Assert.Equal(0, constrained.OffsetYPercent, 6);
    }

    [Fact]
    public void ConstrainTransform_DoesNotConstrainRootContainer()
    {
        var bounds = Bounds("ROOT", null, 20, 30, 40, 20, 100, 100);
        var interaction = new MacCanvasInteraction("ROOT", "resize", 175, -125, 4, 3, 45);

        Assert.Same(interaction, MacCanvasBoundary.ConstrainTransform(bounds, interaction));
    }

    [Fact]
    public void FlowLayout_HorizontalDropUsesVisualCenters()
    {
        var parent = new WMContainer { ID = "PARENT", Orientation = Orientation.Horizontal };
        var first = new WMText { ID = "FIRST" };
        var second = new WMText { ID = "SECOND" };
        var third = new WMText { ID = "THIRD" };
        parent.Controls.AddRange([first, second, third]);
        var bounds = new[]
        {
            Bounds(first.ID, parent.ID, 0, 0, 10, 10, 100, 100),
            Bounds(second.ID, parent.ID, 20, 0, 10, 10, 100, 100),
            Bounds(third.ID, parent.ID, 40, 0, 10, 10, 100, 100)
        };
        var interaction = new MacCanvasInteraction(first.ID, "drag", 50, 0, 1, 1, 0);

        var index = MacCanvasFlowLayout.GetDropIndex(parent, first, bounds[0], interaction, bounds);

        Assert.Equal(2, index);
    }

    [Fact]
    public void FlowLayout_VerticalDropUsesConfiguredAxis()
    {
        var parent = new WMContainer { ID = "PARENT", Orientation = Orientation.Vertical };
        var first = new WMText { ID = "FIRST" };
        var second = new WMText { ID = "SECOND" };
        var third = new WMText { ID = "THIRD" };
        parent.Controls.AddRange([first, second, third]);
        var bounds = new[]
        {
            Bounds(first.ID, parent.ID, 0, 0, 10, 10, 100, 100),
            Bounds(second.ID, parent.ID, 0, 20, 10, 10, 100, 100),
            Bounds(third.ID, parent.ID, 0, 40, 10, 10, 100, 100)
        };
        var interaction = new MacCanvasInteraction(third.ID, "drag", 0, -45, 1, 1, 0);

        var index = MacCanvasFlowLayout.GetDropIndex(parent, third, bounds[2], interaction, bounds);

        Assert.Equal(0, index);
    }

    private static WMDesignBounds Bounds(
        string id,
        string? parentId,
        double x,
        double y,
        double width,
        double height,
        double parentWidth,
        double parentHeight) =>
        new(id, parentId, "WMText", x, y, width, height, parentWidth, parentHeight, new WMTransform(), false, true);
}
