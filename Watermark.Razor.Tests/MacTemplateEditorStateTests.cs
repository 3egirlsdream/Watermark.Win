using Watermark.Razor.Components.Mac.Editor;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacTemplateEditorStateTests
{
    [Fact]
    public void Create_UsesIndependentDraft()
    {
        var original = new WMCanvas { Name = "original" };
        var state = MacTemplateEditorState.Create(original);
        state.Mutate("rename", () => state.Draft.Name = "draft");
        Assert.Equal("original", original.Name);
        Assert.Equal("draft", state.Draft.Name);
    }

    [Fact]
    public void Transaction_CoalescesContinuousChanges()
    {
        var state = MacTemplateEditorState.Create(new WMCanvas { Name = "one" });
        state.BeginTransaction("slider");
        state.Draft.Name = "two";
        state.Draft.Name = "three";
        state.CommitTransaction();
        Assert.Equal(2, state.HistoryCount);
        Assert.True(state.Undo());
        Assert.Equal("one", state.Draft.Name);
    }

    [Fact]
    public void SavedSnapshot_DrivesDirtyState()
    {
        var state = MacTemplateEditorState.Create(new WMCanvas { Name = "one" });
        state.Mutate("rename", () => state.Draft.Name = "two");
        Assert.True(state.IsDirty);
        state.MarkSaved();
        Assert.False(state.IsDirty);
        state.Undo();
        Assert.True(state.IsDirty);
        state.Redo();
        Assert.False(state.IsDirty);
    }

    [Fact]
    public void History_CapsSnapshotsAndTruncatesRedoAfterNewMutation()
    {
        var state = MacTemplateEditorState.Create(new WMCanvas { Name = "0" });
        for (var i = 1; i <= 55; i++)
            state.Mutate("rename", () => state.Draft.Name = i.ToString());

        Assert.Equal(50, state.HistoryCount);
        Assert.True(state.Undo());
        state.Mutate("rename", () => state.Draft.Name = "replacement");
        Assert.False(state.CanRedo);
        Assert.Equal("replacement", state.Draft.Name);
    }

    [Fact]
    public void CancelTransaction_RestoresDraftWithoutAddingHistory()
    {
        var state = MacTemplateEditorState.Create(new WMCanvas { Name = "one" });
        state.BeginTransaction("slider");
        state.Draft.Name = "two";
        state.CancelTransaction();

        Assert.Equal("one", state.Draft.Name);
        Assert.Equal(1, state.HistoryCount);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public void CommitTransaction_WithNoChangesIsNoOp()
    {
        var state = MacTemplateEditorState.Create(new WMCanvas { Name = "one" });
        state.BeginTransaction("slider");

        Assert.False(state.CommitTransaction());
        Assert.Equal(1, state.HistoryCount);
    }

    [Fact]
    public void Create_PreservesRuntimeStateWithoutAliasingOriginal()
    {
        var original = RuntimeCanvas();
        var originalText = Assert.IsType<WMText>(MacControlTree.Find(original, "TEXT"));
        var state = MacTemplateEditorState.Create(original);
        var draftText = Assert.IsType<WMText>(MacControlTree.Find(state.Draft, "TEXT"));

        AssertRuntimeState(state.Draft, "original", "value", 10, 20, 30, 40);
        state.Draft.Path = "draft";
        state.Draft.Exif["camera"]["model"] = "draft-value";
        draftText.Width = 50;
        draftText.Height = 60;
        draftText.DesignX = 70;
        draftText.DesignY = 80;

        AssertRuntimeState(original, "original", "value", 10, 20, 30, 40);
        Assert.NotSame(originalText, draftText);
    }

    [Fact]
    public void CancelTransaction_RestoresPathExifAndGeometry()
    {
        var original = RuntimeCanvas();
        var state = MacTemplateEditorState.Create(original);
        var text = Assert.IsType<WMText>(MacControlTree.Find(state.Draft, "TEXT"));
        state.BeginTransaction("runtime");
        state.Draft.Path = "changed";
        state.Draft.Exif["camera"]["model"] = "changed-value";
        text.Width = 50;
        text.Height = 60;
        text.DesignX = 70;
        text.DesignY = 80;

        state.CancelTransaction();

        AssertRuntimeState(state.Draft, "original", "value", 10, 20, 30, 40);
        state.Draft.Exif["camera"]["model"] = "after-cancel";
        Assert.Equal("value", original.Exif["camera"]["model"]);
    }

    [Fact]
    public void UndoRedo_RestoresPathExifAndGeometry()
    {
        var state = MacTemplateEditorState.Create(RuntimeCanvas());
        state.BeginTransaction("runtime");
        var text = Assert.IsType<WMText>(MacControlTree.Find(state.Draft, "TEXT"));
        state.Draft.Path = "changed";
        state.Draft.Exif["camera"]["model"] = "changed-value";
        text.Width = 50;
        text.Height = 60;
        text.DesignX = 70;
        text.DesignY = 80;

        Assert.True(state.CommitTransaction());
        Assert.True(state.IsDirty);
        Assert.True(state.Undo());
        AssertRuntimeState(state.Draft, "original", "value", 10, 20, 30, 40);
        Assert.True(state.Redo());
        AssertRuntimeState(state.Draft, "changed", "changed-value", 50, 60, 70, 80);
    }

    [Fact]
    public void PathAndExifOnlyTransaction_IsNotNoOp()
    {
        var state = MacTemplateEditorState.Create(RuntimeCanvas());
        state.BeginTransaction("runtime");
        state.Draft.Path = "changed";
        state.Draft.Exif["camera"]["model"] = "changed-value";

        Assert.True(state.CommitTransaction());
        Assert.Equal(2, state.HistoryCount);
        Assert.True(state.IsDirty);
    }

    private static WMCanvas RuntimeCanvas()
    {
        var canvas = new WMCanvas { Path = "original" };
        canvas.Exif["camera"] = new Dictionary<string, string> { ["model"] = "value" };
        var container = new WMContainer { ID = "CONTAINER" };
        container.Controls.Add(new WMText
        {
            ID = "TEXT",
            Width = 10,
            Height = 20,
            DesignX = 30,
            DesignY = 40
        });
        canvas.Children.Add(container);
        return canvas;
    }

    private static void AssertRuntimeState(WMCanvas canvas, string path, string exif, double width, double height, double designX, double designY)
    {
        var text = Assert.IsType<WMText>(MacControlTree.Find(canvas, "TEXT"));
        Assert.Equal(path, canvas.Path);
        Assert.Equal(exif, canvas.Exif["camera"]["model"]);
        Assert.Equal(width, text.Width);
        Assert.Equal(height, text.Height);
        Assert.Equal(designX, text.DesignX);
        Assert.Equal(designY, text.DesignY);
    }
}
