using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMControlTreeTests
{
    [Fact]
    public void Move_ReparentsControlAndKeepsUniqueId()
    {
        var canvas = new WMCanvas();
        var left = new WMContainer { Name = "left" };
        var right = new WMContainer { Name = "right" };
        var text = new WMText { Name = "text" };
        left.Controls.Add(text);
        canvas.Children.AddRange([left, right]);

        Assert.True(WMControlTree.Move(canvas, text.ID, right.ID, 0));
        Assert.Empty(left.Controls);
        Assert.Same(text, right.Controls[0]);
        Assert.Equal(WMControlTree.Flatten(canvas).Count,
            WMControlTree.Flatten(canvas).Select(x => x.ID).Distinct().Count());
    }

    [Fact]
    public void MovePreservingVisualBounds_ConvertsRootLeafIntoParentCoordinates()
    {
        var canvas = new WMCanvas { LayoutSchemaVersion = WMLayoutMigration.CurrentSchemaVersion };
        var target = new WMContainer { ID = "TARGET" };
        target.Style.Position = WMPosition.Absolute;
        target.Style.Padding = new WMThickness { Left = 5, Top = 10, Right = 5 };
        var text = new WMText { ID = "TEXT" };
        text.Style.Position = WMPosition.Absolute;
        text.Style.Transform.OffsetXPercent = 5;
        canvas.Children.AddRange([target, text]);
        var bounds = new[]
        {
            new WMDesignBounds(target.ID, null, nameof(WMContainer), 50, 20, 200, 100, 400, 200, new WMTransform(), false, true),
            new WMDesignBounds(text.ID, null, nameof(WMText), 100, 50, 80, 20, 400, 200, text.Style.Transform, false, true)
        };

        var moved = WMControlTree.MovePreservingVisualBounds(
            canvas,
            text.ID,
            target.ID,
            0,
            bounds,
            400,
            200);

        Assert.True(moved);
        Assert.Same(text, Assert.Single(target.Controls));
        Assert.Equal(WMPosition.Absolute, text.Style.Position);
        Assert.Equal(33.333333, text.Style.Left!.Value, 6);
        Assert.Equal(12.5, text.Style.Top!.Value, 6);
        Assert.Equal(0, text.Style.Transform.OffsetXPercent);
    }

    [Fact]
    public void MovePreservingVisualBounds_InvertsTargetContainerTransform()
    {
        var canvas = new WMCanvas { LayoutSchemaVersion = WMLayoutMigration.CurrentSchemaVersion };
        var target = new WMContainer { ID = "TARGET" };
        target.Style.Position = WMPosition.Absolute;
        target.Style.Transform.Rotation = 90;
        var text = new WMText { ID = "TEXT" };
        text.Style.Position = WMPosition.Absolute;
        canvas.Children.AddRange([target, text]);
        var bounds = new[]
        {
            new WMDesignBounds(
                target.ID,
                null,
                nameof(WMContainer),
                50,
                20,
                200,
                100,
                400,
                200,
                target.Style.Transform,
                false,
                true),
            // This center (170, 30) is target-local (60, 30) after its 90°
            // transform, so preserving it produces 25% Left and Top.
            new WMDesignBounds(
                text.ID,
                null,
                nameof(WMText),
                160,
                25,
                20,
                10,
                400,
                200,
                text.Style.Transform,
                false,
                true)
        };

        Assert.True(WMControlTree.MovePreservingVisualBounds(
            canvas,
            text.ID,
            target.ID,
            0,
            bounds,
            400,
            200));

        Assert.Equal(25, text.Style.Left!.Value, 6);
        Assert.Equal(25, text.Style.Top!.Value, 6);
    }

    [Fact]
    public void Move_ReparentRoundTripsThroughTemplateSerialization()
    {
        var canvas = new WMCanvas();
        var left = new WMContainer { Name = "left" };
        var right = new WMContainer { Name = "right" };
        var text = new WMText
        {
            Name = "text",
            Transform = new WMTransform { OffsetXPercent = 45, OffsetYPercent = -30 }
        };
        left.Controls.Add(text);
        canvas.Children.AddRange([left, right]);

        Assert.True(WMControlTree.Move(canvas, text.ID, right.ID, 0));
        var restored = Global.ReadConfig(Global.CanvasSerialize(canvas));

        Assert.Empty(Assert.IsType<WMContainer>(
            restored.Children.Single(container => container.ID == left.ID)).Controls);
        var restoredText = Assert.IsType<WMText>(Assert.Single(
            Assert.IsType<WMContainer>(
                restored.Children.Single(container => container.ID == right.ID)).Controls));
        Assert.Equal(text.ID, restoredText.ID);
        Assert.Equal(0, restoredText.Transform!.OffsetXPercent);
        Assert.Equal(0, restoredText.Transform.OffsetYPercent);
    }

    [Fact]
    public void CanMove_RejectsSelfAndDescendant()
    {
        var canvas = new WMCanvas();
        var root = new WMContainer { Name = "root" };
        var nested = new WMContainer { Name = "nested" };
        root.Controls.Add(nested);
        canvas.Children.Add(root);

        Assert.False(WMControlTree.CanMove(canvas, root.ID, root.ID, out _));
        Assert.False(WMControlTree.CanMove(canvas, root.ID, nested.ID, out _));
    }

    [Fact]
    public void Duplicate_AssignsNewIdsRecursively()
    {
        var canvas = new WMCanvas();
        var root = new WMContainer { Name = "root" };
        root.Controls.Add(new WMText { Name = "text" });
        canvas.Children.Add(root);

        var duplicate = Assert.IsType<WMContainer>(WMControlTree.Duplicate(canvas, root.ID));
        Assert.NotEqual(root.ID, duplicate.ID);
        Assert.NotEqual(root.Controls[0].ID, duplicate.Controls[0].ID);
        Assert.Equal([0, 1], canvas.Children.Select(control => control.PNode.SEQ));
        Assert.Equal("0", duplicate.PNode.PID);
        Assert.Equal(duplicate.ID, Assert.IsType<WMText>(Assert.Single(duplicate.Controls)).PNode.PID);
    }

    [Fact]
    public void Remove_RenumbersRemainingMixedRootNodes()
    {
        var canvas = new WMCanvas();
        var container = new WMContainer { ID = "CONTAINER" };
        var text = new WMText { ID = "TEXT" };
        var line = new WMLine { ID = "LINE" };
        canvas.Children.AddRange([container, text, line]);

        Assert.True(WMControlTree.Remove(canvas, text.ID));

        Assert.Equal([container, line], canvas.Children);
        Assert.Equal([0, 1], canvas.Children.Select(control => control.PNode.SEQ));
        Assert.All(canvas.Children, control => Assert.Equal("0", control.PNode.PID));
    }

    [Fact]
    public void CanMove_RejectsContainerThatWouldExceedMaximumDepth()
    {
        var canvas = new WMCanvas();
        var moving = new WMContainer();
        var targetRoot = new WMContainer();
        var targetNested = new WMContainer();
        targetRoot.Controls.Add(targetNested);
        canvas.Children.AddRange([moving, targetRoot]);

        Assert.False(WMControlTree.CanMove(canvas, moving.ID, targetNested.ID, out var error));
        Assert.Contains("两级", error);
    }

    [Fact]
    public void Add_TextWithoutParentCreatesRootLeaf()
    {
        var canvas = new WMCanvas { LayoutSchemaVersion = WMLayoutMigration.CurrentSchemaVersion };

        var text = Assert.IsType<WMText>(WMControlTree.Add(canvas, typeof(WMText), null));

        Assert.Same(text, Assert.Single(canvas.Children));
        Assert.Equal(WMPosition.Absolute, text.Style.Position);
        Assert.Equal("0", text.PNode.PID);
        var placeholder = Assert.Single(text.Exifs);
        Assert.Equal("文字", placeholder.Prefix);
        Assert.True(string.IsNullOrWhiteSpace(placeholder.Key));
    }

    [Fact]
    public void Move_InvalidIndexesLeaveSameParentUnchanged()
    {
        var canvas = new WMCanvas();
        var parent = new WMContainer();
        var first = new WMText { Name = "first" };
        var second = new WMText { Name = "second" };
        parent.Controls.AddRange([first, second]);
        canvas.Children.Add(parent);

        Assert.False(WMControlTree.Move(canvas, first.ID, parent.ID, -1));
        Assert.False(WMControlTree.Move(canvas, first.ID, parent.ID, 2));
        Assert.Same(first, parent.Controls[0]);
        Assert.Same(second, parent.Controls[1]);
    }

    [Fact]
    public void Move_SameParentUsesFinalInsertionIndex()
    {
        var canvas = new WMCanvas();
        var parent = new WMContainer();
        var first = new WMText { Name = "first" };
        var second = new WMText { Name = "second" };
        var third = new WMText { Name = "third" };
        second.Margin = new WMThickness { Top = 7, Right = 3, Bottom = 5, Left = 1 };
        parent.Controls.AddRange([first, second, third]);
        canvas.Children.Add(parent);

        Assert.True(WMControlTree.Move(canvas, second.ID, parent.ID, 2));

        Assert.Equal(["first", "third", "second"], parent.Controls.Select(control => control.Name));
        Assert.Equal(7, second.Margin.Top);
        Assert.Equal(3, second.Margin.Right);
        Assert.Equal(5, second.Margin.Bottom);
        Assert.Equal(1, second.Margin.Left);
    }

    [Fact]
    public void Move_TooLargeCrossParentIndexLeavesTreeUnchanged()
    {
        var canvas = new WMCanvas();
        var source = new WMContainer();
        var target = new WMContainer();
        var text = new WMText();
        source.Controls.Add(text);
        canvas.Children.AddRange([source, target]);

        Assert.False(WMControlTree.Move(canvas, text.ID, target.ID, 1));
        Assert.Same(text, Assert.Single(source.Controls));
        Assert.Empty(target.Controls);
    }

    [Fact]
    public void Add_RejectsBaseAndUnsupportedControlTypes()
    {
        var canvas = new WMCanvas();

        Assert.Throws<ArgumentException>(() => WMControlTree.Add(canvas, typeof(IWMControl), null));
        Assert.Throws<ArgumentException>(() => WMControlTree.Add(canvas, typeof(UnsupportedControl), null));
        Assert.Empty(canvas.Children);
    }

    [Fact]
    public void Add_UsesUniqueUppercaseGuidIdsForAllInsertedControls()
    {
        var canvas = new WMCanvas();
        var container = WMControlTree.Add(canvas, typeof(WMContainer), null);
        var text = WMControlTree.Add(canvas, typeof(WMText), null);
        var logo = WMControlTree.Add(canvas, typeof(WMLogo), null);
        var line = WMControlTree.Add(canvas, typeof(WMLine), null);

        var controls = WMControlTree.Flatten(canvas);
        Assert.Equal(controls.Count, controls.Select(control => control.ID).Distinct().Count());
        Assert.All(controls, control =>
        {
            Assert.True(Guid.TryParseExact(control.ID, "N", out _));
            Assert.Equal(control.ID.ToUpperInvariant(), control.ID);
        });
        Assert.Contains(container, controls);
        Assert.Contains(text, controls);
        Assert.Contains(logo, controls);
        Assert.Contains(line, controls);
    }

    private sealed class UnsupportedControl : IWMControl
    {
    }
}
