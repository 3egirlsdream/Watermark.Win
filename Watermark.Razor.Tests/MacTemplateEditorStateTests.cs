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
}
