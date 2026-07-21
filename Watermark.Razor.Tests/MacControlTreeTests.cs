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

        Assert.Empty(restored.Children.Single(container => container.ID == left.ID).Controls);
        var restoredText = Assert.IsType<WMText>(Assert.Single(
            restored.Children.Single(container => container.ID == right.ID).Controls));
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
    public void Add_TextWithoutParentCreatesRootContainer()
    {
        var canvas = new WMCanvas();

        var text = Assert.IsType<WMText>(WMControlTree.Add(canvas, typeof(WMText), null));

        var parent = Assert.Single(canvas.Children);
        Assert.Equal("新容器", parent.Name);
        Assert.Same(text, Assert.Single(parent.Controls));
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
