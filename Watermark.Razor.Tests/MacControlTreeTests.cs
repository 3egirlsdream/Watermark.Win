using Watermark.Razor.Components.Mac.Editor;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacControlTreeTests
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

        Assert.True(MacControlTree.Move(canvas, text.ID, right.ID, 0));
        Assert.Empty(left.Controls);
        Assert.Same(text, right.Controls[0]);
        Assert.Equal(MacControlTree.Flatten(canvas).Count,
            MacControlTree.Flatten(canvas).Select(x => x.ID).Distinct().Count());
    }

    [Fact]
    public void CanMove_RejectsSelfAndDescendant()
    {
        var canvas = new WMCanvas();
        var root = new WMContainer { Name = "root" };
        var nested = new WMContainer { Name = "nested" };
        root.Controls.Add(nested);
        canvas.Children.Add(root);

        Assert.False(MacControlTree.CanMove(canvas, root.ID, root.ID, out _));
        Assert.False(MacControlTree.CanMove(canvas, root.ID, nested.ID, out _));
    }

    [Fact]
    public void Duplicate_AssignsNewIdsRecursively()
    {
        var canvas = new WMCanvas();
        var root = new WMContainer { Name = "root" };
        root.Controls.Add(new WMText { Name = "text" });
        canvas.Children.Add(root);

        var duplicate = Assert.IsType<WMContainer>(MacControlTree.Duplicate(canvas, root.ID));
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

        Assert.False(MacControlTree.CanMove(canvas, moving.ID, targetNested.ID, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Add_TextWithoutParentCreatesRootContainer()
    {
        var canvas = new WMCanvas();

        var text = Assert.IsType<WMText>(MacControlTree.Add(canvas, typeof(WMText), null));

        var parent = Assert.Single(canvas.Children);
        Assert.Equal("新容器", parent.Name);
        Assert.Same(text, Assert.Single(parent.Controls));
    }
}
