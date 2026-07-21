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

        Assert.Equal(100, container.Transform!.OffsetXPercent);
        Assert.Equal(-100, container.Transform.OffsetYPercent);
        Assert.Equal(4, container.Transform.ScaleX);
        Assert.Equal(0.1, container.Transform.ScaleY);
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
    public void Apply_UsesStyleTransformForAbsoluteV2Nodes()
    {
        var logo = new WMLogo { ID = "LOGO" };
        logo.Style.Position = WMPosition.Absolute;

        MacCanvasTransform.Apply(logo, new MacCanvasInteraction(logo.ID, "drag", 12, -8, 1.5, 0.5, 30));

        Assert.Equal(12, logo.Style.Transform.OffsetXPercent);
        Assert.Equal(-8, logo.Style.Transform.OffsetYPercent);
        Assert.Equal(1.5, logo.Style.Transform.ScaleX);
        Assert.Equal(0.5, logo.Style.Transform.ScaleY);
        Assert.Equal(30, logo.Style.Transform.Rotation);
        Assert.Null(logo.Transform);
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
    public void FlowLayout_V2ReordersBeforeCalculatingCanvasUnitMargins()
    {
        var parent = new WMContainer { ID = "PARENT" };
        parent.Style.FlexDirection = Orientation.Horizontal;
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

        var drop = MacCanvasFlowLayout.ResolveV2Drop(
            parent,
            first,
            bounds[0],
            new MacCanvasInteraction(first.ID, "drag", 50, 0, 1, 1, 0),
            bounds,
            100);

        Assert.Equal(2, drop.Index);
        Assert.InRange(drop.Left, -25, 25);
        Assert.InRange(drop.Right, -25, 25);
    }

    [Fact]
    public void FlowLayout_ApplyDropPersistsV2OrderAndStyleMargins()
    {
        var canvas = new WMCanvas { LayoutSchemaVersion = WMLayoutMigration.CurrentSchemaVersion };
        var parent = new WMContainer { ID = "PARENT" };
        parent.Style.Position = WMPosition.Absolute;
        var first = new WMText { ID = "FIRST" };
        var second = new WMText { ID = "SECOND" };
        parent.Controls.AddRange([first, second]);
        canvas.Children.Add(parent);

        var applied = MacCanvasFlowLayout.ApplyDrop(
            canvas,
            parent,
            first,
            new MacCanvasFlowLayout.DropResult(1, 4.5, -2, -4.5, 2));

        Assert.True(applied);
        Assert.Equal([second, first], parent.Controls);
        Assert.Equal(4.5, first.Style.Margin.Left);
        Assert.Equal(-2, first.Style.Margin.Top);
        Assert.Equal(-4.5, first.Style.Margin.Right);
        Assert.Equal(2, first.Style.Margin.Bottom);
        Assert.Equal(0, first.Margin.Left);

        var restored = Global.ReadConfig(Global.CanvasSerialize(canvas));
        var restoredFirst = Assert.IsType<WMText>(Watermark.Razor.Workspace.WMControlTree.Find(restored, first.ID));
        Assert.Equal(4.5, restoredFirst.Style.Margin.Left);
        Assert.Equal(-4.5, restoredFirst.Style.Margin.Right);
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

    [Fact]
    public void FlowLayout_DropReordersBeforeCalculatingHorizontalMargins()
    {
        var parent = new WMContainer
        {
            ID = "PARENT",
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        var first = new WMText { ID = "FIRST" };
        var second = new WMText { ID = "SECOND" };
        parent.Controls.AddRange([first, second]);
        var bounds = new[]
        {
            Bounds(first.ID, parent.ID, 0, 45, 10, 10, 100, 100),
            Bounds(second.ID, parent.ID, 10, 45, 10, 10, 100, 100)
        };

        var drop = MacCanvasFlowLayout.ResolveDrop(
            parent,
            first,
            bounds[0],
            new MacCanvasInteraction(first.ID, "drag", 25, 0, 1, 1, 0),
            bounds);

        Assert.Equal(1, drop.Index);
        // The new slot starts at X=10. The remaining 15px, rather than the
        // original 25px pointer movement, becomes the stored margin offset.
        Assert.Equal(7.5, drop.Left, 6);
        Assert.Equal(-7.5, drop.Right, 6);
        Assert.Equal(0, drop.Top, 6);
        Assert.Equal(0, drop.Bottom, 6);
    }

    [Fact]
    public void FlowLayout_DropReordersBeforeCalculatingCenteredVerticalMargins()
    {
        var parent = new WMContainer
        {
            ID = "PARENT",
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var first = new WMText { ID = "FIRST" };
        var second = new WMText { ID = "SECOND" };
        parent.Controls.AddRange([first, second]);
        var bounds = new[]
        {
            Bounds(first.ID, parent.ID, 45, 40, 10, 10, 100, 100),
            Bounds(second.ID, parent.ID, 45, 50, 10, 10, 100, 100)
        };

        var drop = MacCanvasFlowLayout.ResolveDrop(
            parent,
            first,
            bounds[0],
            new MacCanvasInteraction(first.ID, "drag", 0, 20, 1, 1, 0),
            bounds);

        Assert.Equal(1, drop.Index);
        // After the swap, FIRST already starts at Y=50. Only its remaining
        // 10px offset is written, with matched top/bottom margins so siblings
        // keep their centered flow positions.
        Assert.Equal(10, drop.Top, 6);
        Assert.Equal(10, drop.Bottom, 6);
        Assert.Equal(0, drop.Left, 6);
        Assert.Equal(0, drop.Right, 6);
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
