using System.Text.Json;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMTemplateEditorDraftTests
{
    [Fact]
    public void DraftState_RoundTripsHistoryCursorSelectionAndRuntimeGeometry()
    {
        var original = CreateCanvas();
        var state = WMTemplateEditorState.Create(original);
        state.Select("TEXT");
        state.Mutate("first", () => state.Draft.Name = "first");
        state.BeginTransaction("gesture");
        var text = Assert.IsType<WMText>(WMControlTree.Find(state.Draft, "TEXT"));
        text.Width = 88;
        text.DesignX = 33;
        Assert.True(state.CommitTransaction());
        Assert.True(state.Undo());

        Assert.True(state.TryExportDraftState(out var exported));
        var json = JsonSerializer.Serialize(exported);
        var persisted = JsonSerializer.Deserialize<WMTemplateEditorDraftState>(json);
        var restored = WMTemplateEditorState.Restore(original, Assert.IsType<WMTemplateEditorDraftState>(persisted));

        Assert.Equal("first", restored.Draft.Name);
        Assert.Equal("TEXT", restored.SelectedControlId);
        Assert.True(restored.CanUndo);
        Assert.True(restored.CanRedo);
        Assert.True(restored.IsDirty);
        Assert.True(restored.Redo());
        var restoredText = Assert.IsType<WMText>(WMControlTree.Find(restored.Draft, "TEXT"));
        Assert.Equal(88, restoredText.Width);
        Assert.Equal(33, restoredText.DesignX);
    }

    [Fact]
    public void DraftState_IsNotPublishedDuringActiveGesture()
    {
        var state = WMTemplateEditorState.Create(CreateCanvas());
        state.BeginTransaction("gesture");
        state.Draft.Name = "moving";

        Assert.False(state.TryExportDraftState(out var active));
        Assert.Null(active);
        Assert.True(state.CommitTransaction());
        Assert.True(state.TryExportDraftState(out var committed));
        Assert.NotNull(committed);
    }

    private static WMCanvas CreateCanvas()
    {
        var canvas = new WMCanvas { ID = "CANVAS", Name = "original", Path = "default.jpg" };
        canvas.Exif["camera"] = new Dictionary<string, string> { ["model"] = "demo" };
        var container = new WMContainer { ID = "CONTAINER" };
        container.Controls.Add(new WMText
        {
            ID = "TEXT",
            Width = 42,
            Height = 20,
            DesignX = 5,
            DesignY = 7
        });
        canvas.Children.Add(container);
        return canvas;
    }
}

