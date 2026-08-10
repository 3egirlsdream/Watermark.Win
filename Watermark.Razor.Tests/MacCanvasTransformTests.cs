using SkiaSharp;
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
    public void ConstrainDrag_KeepsRootContainerPartiallyVisible()
    {
        var bounds = Bounds("ROOT", null, 20, 30, 40, 20, 100, 100);
        var interaction = new MacCanvasInteraction("ROOT", "drag", 175, -125, 1, 1, 0);

        var constrained = MacCanvasBoundary.ConstrainDrag(bounds, interaction);

        Assert.Equal(60, constrained.OffsetXPercent, 6);
        Assert.Equal(-40, constrained.OffsetYPercent, 6);
    }

    [Fact]
    public void ConstrainFlowDrag_KeepsRootContainerFullyInsideCanvas()
    {
        var bounds = Bounds("ROOT", null, 20, 30, 40, 20, 100, 100);
        var interaction = new MacCanvasInteraction("ROOT", "drag", 175, -125, 1, 1, 0);

        var constrained = MacCanvasBoundary.ConstrainFlowDrag(bounds, interaction);

        Assert.Equal(40, constrained.OffsetXPercent, 6);
        Assert.Equal(-30, constrained.OffsetYPercent, 6);
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
    public void ConstrainTransform_KeepsRotatedRootPartiallyVisible()
    {
        var bounds = Bounds("ROOT", null, 20, 30, 40, 20, 100, 100);
        var interaction = new MacCanvasInteraction("ROOT", "resize", 175, -125, 4, 3, 45);

        var constrained = MacCanvasBoundary.ConstrainTransform(bounds, interaction);

        Assert.InRange(constrained.OffsetXPercent, 113, 115);
        Assert.InRange(constrained.OffsetYPercent, -95, -93);
    }

    [Fact]
    public void Resize_ContainerWritesStyleSizeAndPreservesScale()
    {
        var container = new WMContainer { ID = "ROOT" };
        container.Style.Position = WMPosition.Absolute;
        container.Style.Transform.ScaleX = 1.5;
        container.Style.Transform.ScaleY = .75;
        var bounds = Bounds(container.ID, null, 10, 20, 100, 50, 400, 200);

        var applied = MacCanvasResize.Apply(
            container,
            bounds,
            new MacCanvasInteraction(
                container.ID,
                "resize",
                0,
                0,
                1.5,
                .75,
                0,
                160,
                80,
                "se",
                30,
                15));

        Assert.Equal(40, container.Style.Width.Value, 6);
        Assert.Equal(40, container.Style.Height.Value, 6);
        Assert.Equal(1.5, container.Style.Transform.ScaleX);
        Assert.Equal(.75, container.Style.Transform.ScaleY);
        Assert.Equal(160, applied.Width, 6);
        Assert.Equal(80, applied.Height, 6);
    }

    [Fact]
    public void Resize_NestedComponentStopsAtParentEdge()
    {
        var logo = new WMLogo { ID = "LOGO" };
        logo.Style.Position = WMPosition.Absolute;
        var bounds = Bounds(logo.ID, "PARENT", 40, 20, 100, 50, 300, 160);

        var applied = MacCanvasResize.Apply(
            logo,
            bounds,
            new MacCanvasInteraction(
                logo.ID,
                "resize",
                0,
                0,
                1,
                1,
                0,
                Width: 400,
                Height: 50,
                Handle: "e",
                CenterDeltaX: 150));

        Assert.Equal(260, applied.Width, 3);
        Assert.Equal(260d / 300d * 100d, logo.Style.Width.Value, 3);
        Assert.Equal(40d / 300d * 100d, logo.Style.Left!.Value, 3);
        Assert.Equal(80, applied.CenterDeltaX, 3);
    }

    [Fact]
    public void Resize_TextHorizontalHandleSetsWidthButKeepsAutoHeight()
    {
        var text = new WMText { ID = "TEXT", FontSize = 4 };
        text.Style.Position = WMPosition.Absolute;
        var bounds = Bounds(text.ID, null, 0, 0, 80, 20, 400, 200);

        MacCanvasResize.Apply(
            text,
            bounds,
            new MacCanvasInteraction(
                text.ID, "resize", 0, 0, 1, 1, 0,
                Width: 120, Height: 20, Handle: "e"));

        Assert.Equal(30, text.Style.Width.Value, 6);
        Assert.True(text.Style.Height.IsAuto);
        Assert.Equal(4, text.FontSize);
    }

    [Fact]
    public void Resize_TextCornerChangesFontSizeWithoutScalingDecorations()
    {
        var text = new WMText
        {
            ID = "TEXT",
            FontSize = 4,
            BorderWidth = 3,
            BorderPadding = 5,
            BorderRadius = 7
        };
        text.Style.Position = WMPosition.Absolute;
        var bounds = Bounds(text.ID, null, 0, 0, 80, 20, 400, 200);

        MacCanvasResize.Apply(
            text,
            bounds,
            new MacCanvasInteraction(
                text.ID, "resize", 0, 0, 1, 1, 0,
                Width: 160, Height: 40, Handle: "se"));

        Assert.Equal(8, text.FontSize);
        Assert.Equal(3, text.BorderWidth);
        Assert.Equal(5, text.BorderPadding);
        Assert.Equal(7, text.BorderRadius);
        Assert.True(text.Style.Width.IsAuto);
        Assert.True(text.Style.Height.IsAuto);
    }

    [Fact]
    public void Resize_TextCornerUsesTheLiveContentScaleAndKeepsAutoHeight()
    {
        var text = new WMText
        {
            ID = "TEXT",
            FontSize = 10,
            EnableBorder = true,
            BorderWidth = 3,
            BorderPadding = 7
        };
        text.Style.Position = WMPosition.Absolute;
        text.Style.Width = WMStyleLength.Percent(75);
        var bounds = Bounds(text.ID, null, 20, 30, 300, 100, 400, 200);

        var applied = MacCanvasResize.Apply(
            text,
            bounds,
            new MacCanvasInteraction(
                text.ID, "resize", 0, 0, 1, 1, 0,
                Width: 524, Height: 164, Handle: "se",
                CenterDeltaX: 112, CenterDeltaY: 32,
                ResizeRatio: 1.8));

        Assert.Equal(18, text.FontSize, 6);
        Assert.Equal(131, text.Style.Width.Value, 6);
        Assert.True(text.Style.Height.IsAuto);
        Assert.Equal(5, text.Style.Left!.Value, 6);
        Assert.Equal(15, text.Style.Top!.Value, 6);
        Assert.Equal(524, applied.Width, 6);
        Assert.Equal(164, applied.Height, 6);
    }

    [Fact]
    public void Resize_StaticLineChangesLengthWithoutCreatingTransform()
    {
        var line = new WMLine { ID = "LINE", Orientation = Orientation.Horizontal };
        line.Style.Position = WMPosition.Static;
        var bounds = Bounds(line.ID, "PARENT", 0, 0, 80, 2, 400, 200);

        MacCanvasResize.Apply(
            line,
            bounds,
            new MacCanvasInteraction(
                line.ID, "resize", 0, 0, 1, 1, 0,
                Width: 200, Height: 2, Handle: "e"));

        Assert.Equal(50, line.Style.Width.Value, 6);
        Assert.True(line.Style.Height.IsAuto);
        Assert.Equal(50, line.LengthPercent, 6);
        Assert.Equal(50, line.Percent, 6);
        Assert.Null(line.Transform);
        Assert.Equal(1, line.Style.Transform.ScaleX);
    }

    [Fact]
    public void Resize_VerticalLineUpdatesOnlyHeightAndCompatibilityMirror()
    {
        var line = new WMLine { ID = "LINE", Orientation = Orientation.Vertical, Percent = 25 };
        line.Style.Position = WMPosition.Absolute;
        line.Style.Width = WMStyleLength.Percent(75);
        var bounds = Bounds(line.ID, null, 10, 20, 4, 50, 400, 200);

        MacCanvasResize.Apply(
            line,
            bounds,
            new MacCanvasInteraction(
                line.ID, "resize", 0, 0, 1, 1, 0,
                Width: 4, Height: 120, Handle: "s"));

        Assert.True(line.Style.Width.IsAuto);
        Assert.Equal(60, line.Style.Height.Value, 6);
        Assert.Equal(60, line.LengthPercent, 6);
        Assert.Equal(60, line.Percent, 6);
    }

    [Fact]
    public void Resize_HorizontalLineIgnoresPerpendicularPointerNoise()
    {
        var line = new WMLine { ID = "LINE", Orientation = Orientation.Horizontal };
        line.Style.Position = WMPosition.Absolute;
        var bounds = Bounds(line.ID, null, 40, 60, 100, 4, 400, 200);

        var applied = MacCanvasResize.Apply(
            line,
            bounds,
            new MacCanvasInteraction(
                line.ID, "resize", 0, 0, 1, 1, 0,
                Width: 160, Height: 7, Handle: "e",
                CenterDeltaX: 30, CenterDeltaY: -1.25));

        Assert.Equal(40, line.LengthPercent, 6);
        Assert.Equal(10, line.Style.Left!.Value, 6);
        Assert.Equal(30, line.Style.Top!.Value, 6);
        Assert.Equal(30, applied.CenterDeltaX, 6);
        Assert.Equal(0, applied.CenterDeltaY, 6);
        Assert.Equal(160, applied.Width, 6);
        Assert.Equal(4, applied.Height, 6);
    }

    [Fact]
    public void Resize_LogoSideHandleChangesOnlyTheDraggedImageAxis()
    {
        var logo = new WMLogo { ID = "LOGO" };
        logo.Style.Position = WMPosition.Absolute;
        var bounds = Bounds(logo.ID, null, 0, 0, 100, 50, 400, 200);

        MacCanvasResize.Apply(
            logo,
            bounds,
            new MacCanvasInteraction(
                logo.ID, "resize", 0, 0, 1, 1, 0,
                Width: 160, Height: 50, Handle: "e", KeepAspectRatio: true));

        Assert.Equal(40, logo.Style.Width.Value, 6);
        Assert.True(logo.Style.Height.IsAuto);
    }

    [Fact]
    public void Resize_LogoCornerHandleKeepsAspectRatioWhenLocked()
    {
        var logo = new WMLogo { ID = "LOGO" };
        logo.Style.Position = WMPosition.Absolute;
        var bounds = Bounds(logo.ID, null, 0, 0, 100, 50, 400, 200);

        MacCanvasResize.Apply(
            logo,
            bounds,
            new MacCanvasInteraction(
                logo.ID, "resize", 0, 0, 1, 1, 0,
                Width: 160, Height: 80, Handle: "se", KeepAspectRatio: true));

        Assert.Equal(40, logo.Style.Width.Value, 6);
        Assert.Equal(40, logo.Style.Height.Value, 6);
    }

    [Fact]
    public void Resize_LogoCornerPersistsExactLiveProxyGeometry()
    {
        var logo = new WMLogo { ID = "LOGO" };
        logo.Style.Position = WMPosition.Absolute;
        var bounds = Bounds(logo.ID, null, 10, 20, 100, 50, 400, 200);

        var applied = MacCanvasResize.Apply(
            logo,
            bounds,
            new MacCanvasInteraction(
                logo.ID, "resize", 0, 0, 1, 1, 0,
                Width: 40, Height: 20, Handle: "nw",
                CenterDeltaX: 30, CenterDeltaY: 15,
                KeepAspectRatio: true));

        Assert.Equal(10, logo.Style.Width.Value, 6);
        Assert.Equal(10, logo.Style.Height.Value, 6);
        Assert.Equal(17.5, logo.Style.Left!.Value, 6);
        Assert.Equal(25, logo.Style.Top!.Value, 6);
        Assert.Equal(40, applied.Width, 6);
        Assert.Equal(20, applied.Height, 6);
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
    public void ApplyFlow_MovesChildWithoutChangingItsFlexSlotOrParent()
    {
        var parent = new WMContainer { ID = "PARENT" };
        parent.Style.Position = WMPosition.Absolute;
        parent.Style.Transform.OffsetXPercent = 7;
        var first = new WMText { ID = "FIRST" };
        var dragged = new WMText { ID = "DRAGGED" };
        dragged.Style.Margin = new WMThickness(3, 4, 5, 6);
        parent.Controls.AddRange([first, dragged]);
        var bounds = Bounds(dragged.ID, parent.ID, 20, 30, 40, 20, 100, 80);

        var applied = MacCanvasBoundary.ConstrainDrag(
            bounds,
            new MacCanvasInteraction(dragged.ID, "drag", 100, -100, 1, 1, 0));
        MacCanvasTransform.ApplyFlow(dragged, applied);

        Assert.Equal(WMPosition.Static, dragged.Style.Position);
        Assert.Equal([first, dragged], parent.Controls);
        Assert.Equal(7, parent.Style.Transform.OffsetXPercent);
        Assert.Equal(3, dragged.Style.Margin.Left);
        Assert.Equal(4, dragged.Style.Margin.Top);
        Assert.Equal(5, dragged.Style.Margin.Right);
        Assert.Equal(6, dragged.Style.Margin.Bottom);
        Assert.Equal(40, applied.OffsetXPercent, 6);
        Assert.Equal(-37.5, applied.OffsetYPercent, 6);
        Assert.Equal(applied.OffsetXPercent, dragged.Style.Transform.OffsetXPercent, 6);
        Assert.Equal(applied.OffsetYPercent, dragged.Style.Transform.OffsetYPercent, 6);
    }

    [Fact]
    public async Task FlowTransform_MovesNestedPixelsWithoutReflowingSibling()
    {
        var canvas = new WMCanvas
        {
            ID = "CANVAS",
            LayoutSchemaVersion = WMLayoutMigration.CurrentSchemaVersion,
            CanvasType = CanvasType.Split,
            CustomWidth = 400,
            CustomHeight = 300,
            BackgroundColor = "#FFFFFFFF",
            ImageProperties = new WMImage { Show = false }
        };
        var parent = new WMContainer
        {
            ID = "PARENT",
            BackgroundColor = "#FFFFFFFF",
            Style = new WMStyle
            {
                Position = WMPosition.Absolute,
                Left = WMStyleLength.Percent(10),
                Top = WMStyleLength.Percent(60),
                Width = WMStyleLength.Percent(80),
                Height = WMStyleLength.Percent(30),
                Overflow = WMOverflow.Hidden,
                FlexDirection = Orientation.Horizontal,
                AlignItems = WMAlignItems.Center
            }
        };
        var nested = new WMContainer
        {
            ID = "NESTED",
            Style = new WMStyle
            {
                Width = WMStyleLength.Percent(50),
                Height = WMStyleLength.Percent(100),
                Overflow = WMOverflow.Hidden,
                AlignItems = WMAlignItems.Center,
                JustifyContent = WMJustifyContent.Center
            }
        };
        nested.Controls.Add(new WMLine
        {
            ID = "CHILD",
            Orientation = Orientation.Horizontal,
            Color = "#E53935FF",
            Thickness = 12,
            Style = new WMStyle
            {
                Width = WMStyleLength.Percent(70),
                Height = WMStyleLength.Percent(20)
            }
        });
        var sibling = new WMLine
        {
            ID = "SIBLING",
            Orientation = Orientation.Vertical,
            Color = "#1976D2FF",
            Thickness = 8,
            Style = new WMStyle
            {
                Width = WMStyleLength.Percent(10),
                Height = WMStyleLength.Percent(60)
            }
        };
        parent.Controls.AddRange([nested, sibling]);
        canvas.Children.Add(parent);
        var renderer = new WMDesignSceneRenderer(new WatermarkHelper());
        await using var session = await renderer.OpenSessionAsync(canvas);
        var initialParent = Assert.Single(session.CurrentFrame.Layers, layer => layer.NodeId == parent.ID);
        var initial = Assert.Single(session.CurrentFrame.Layers, layer => layer.NodeId == nested.ID);
        var initialChild = Assert.Single(session.CurrentFrame.Layers, layer => layer.NodeId == "CHILD");
        var initialSibling = Assert.Single(session.CurrentFrame.Layers, layer => layer.NodeId == sibling.ID);
        Assert.True(initialParent.HasSurface);
        Assert.False(initial.HasSurface);
        Assert.False(initialChild.HasSurface);
        Assert.False(initialSibling.HasSurface);
        Assert.True(ContainsRedPixels(initialParent.SurfaceBytes));
        var initialRedX = RedPixelCentroidX(initialParent.SurfaceBytes);

        var constrained = MacCanvasBoundary.ConstrainDrag(
            initial.Bounds,
            new MacCanvasInteraction(nested.ID, "drag", 20, 0, 1, 1, 0));
        MacCanvasTransform.ApplyFlow(nested, constrained);
        var update = await session.UpdateAsync(
            canvas,
            new WMTemplateChangeSet(
                2,
                WMTemplateChangePhase.Commit,
                WMTemplateChangeKind.Geometry,
                [nested.ID],
                "父容器内自由移动"),
            WMDesignSceneQuality.Exact);

        var movedParent = Assert.Single(update.Frame.Layers, layer => layer.NodeId == parent.ID);
        var moved = Assert.Single(update.Frame.Layers, layer => layer.NodeId == nested.ID);
        var movedSibling = Assert.Single(update.Frame.Layers, layer => layer.NodeId == sibling.ID);
        Assert.True(movedParent.HasSurface);
        Assert.False(moved.HasSurface);
        Assert.True(ContainsRedPixels(movedParent.SurfaceBytes));
        var movedRedX = RedPixelCentroidX(movedParent.SurfaceBytes);
        Assert.NotEqual(initialParent.SurfaceKey, movedParent.SurfaceKey);
        Assert.True(movedRedX > initialRedX + 20, $"Red child did not move: {initialRedX} -> {movedRedX}");
        Assert.NotEqual(initial.Bounds.Transform.OffsetXPercent, moved.Bounds.Transform.OffsetXPercent);
        Assert.Equal(initialSibling.Bounds.X, movedSibling.Bounds.X, 6);
        Assert.Equal(initialSibling.Bounds.Y, movedSibling.Bounds.Y, 6);
        Assert.Equal(initialParent.Bounds.X, movedParent.Bounds.X, 6);
        Assert.Equal(initialParent.Bounds.Y, movedParent.Bounds.Y, 6);
        Assert.Equal(WMPosition.Static, nested.Style.Position);
        Assert.Equal(1, update.RasterizedLayerCount);
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

    private static bool ContainsRedPixels(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return false;
        using var bitmap = SKBitmap.Decode(bytes);
        if (bitmap is null) return false;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.Red > 180 && color.Green < 120 && color.Blue < 120 && color.Alpha > 180)
                    return true;
            }
        }

        return false;
    }

    private static double RedPixelCentroidX(byte[]? bytes)
    {
        Assert.NotNull(bytes);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        var sum = 0d;
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.Alpha > 0 && pixel.Red > 180 && pixel.Red > pixel.Green * 1.5 && pixel.Red > pixel.Blue * 1.5)
            {
                sum += x;
                count++;
            }
        }

        Assert.True(count > 0);
        return sum / count;
    }

}
