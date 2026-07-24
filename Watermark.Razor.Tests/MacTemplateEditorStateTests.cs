using Watermark.Razor.Workspace;
using Watermark.Razor.Components.Mac.Editor;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMTemplateEditorStateTests
{
    [Fact]
    public void Create_UsesIndependentDraft()
    {
        var original = new WMCanvas { Name = "original" };
        var state = WMTemplateEditorState.Create(original);
        state.Mutate("rename", () => state.Draft.Name = "draft");
        Assert.Equal("original", original.Name);
        Assert.Equal("draft", state.Draft.Name);
    }

    [Fact]
    public void Create_ConvertsLegacyLeafMetricsToV2CanvasUnits()
    {
        var original = new WMCanvas { CustomWidth = 1080, CustomHeight = 864 };
        var root = new WMContainer { WidthPercent = 55, HeightPercent = 13 };
        root.Controls.Add(new WMText { ID = "TEXT", FontSize = 28 });
        root.Controls.Add(new WMLogo { ID = "LOGO", Percent = 20 });
        original.Children.Add(root);

        var state = WMTemplateEditorState.Create(original);
        var text = Assert.IsType<WMText>(WMControlTree.Find(state.Draft, "TEXT"));
        var logo = Assert.IsType<WMLogo>(WMControlTree.Find(state.Draft, "LOGO"));
        var legacyShortEdge = 864d * 13 / 100d;

        Assert.Equal(WMLayoutMigration.CurrentSchemaVersion, state.Draft.LayoutSchemaVersion);
        Assert.Equal(28 * legacyShortEdge * 100 / (156 * 864), text.FontSize, 10);
        Assert.Equal(20 * legacyShortEdge / 864, logo.Percent, 10);
        Assert.Equal(28, Assert.IsType<WMText>(original.Children[0].Controls[0]).FontSize);
        Assert.Equal(20, Assert.IsType<WMLogo>(original.Children[0].Controls[1]).Percent);
    }

    [Fact]
    public void Transaction_CoalescesContinuousChanges()
    {
        var state = WMTemplateEditorState.Create(new WMCanvas { Name = "one" });
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
        var state = WMTemplateEditorState.Create(new WMCanvas { Name = "one" });
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
        var state = WMTemplateEditorState.Create(new WMCanvas { Name = "0" });
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
        var state = WMTemplateEditorState.Create(new WMCanvas { Name = "one" });
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
        var state = WMTemplateEditorState.Create(new WMCanvas { Name = "one" });
        var changedCount = 0;
        var detailedChanges = new List<WMTemplateChangeSet>();
        state.Changed += () => changedCount++;
        state.DetailedChanged += detailedChanges.Add;
        state.BeginTransaction("slider");

        Assert.False(state.CommitTransaction());
        Assert.Equal(1, state.HistoryCount);
        Assert.False(state.IsTransactionActive);
        Assert.Equal(2, changedCount);
        Assert.Single(detailedChanges);
        Assert.Equal(WMTemplateChangePhase.Begin, detailedChanges[0].Phase);
    }

    [Fact]
    public void DetailedChanges_PreserveTransactionPhaseKindAndNodeIds()
    {
        var canvas = new WMCanvas();
        canvas.Children.Add(new WMContainer { ID = "ROOT" });
        var state = WMTemplateEditorState.Create(canvas);
        var changes = new List<WMTemplateChangeSet>();
        state.DetailedChanged += changes.Add;

        state.BeginTransaction(
            "move",
            WMTemplateChangeKind.Geometry,
            ["ROOT"]);
        state.Mutate(
            "move",
            () => state.Draft.Children[0].Style.Transform.OffsetXPercent = 5,
            WMTemplateChangeKind.Geometry,
            ["ROOT"]);
        Assert.True(state.CommitTransaction());

        Assert.Equal(
            [
                WMTemplateChangePhase.Begin,
                WMTemplateChangePhase.Update,
                WMTemplateChangePhase.Commit
            ],
            changes.Select(change => change.Phase));
        Assert.All(changes, change => Assert.Equal(WMTemplateChangeKind.Geometry, change.Kind));
        Assert.All(changes, change => Assert.Equal(["ROOT"], change.NodeIds));
        Assert.True(changes[1].Revision > changes[0].Revision);
        Assert.True(changes[2].Revision > changes[1].Revision);
    }

    [Fact]
    public void CommitTransaction_CanEscalateFinalInvalidationWithoutChangingUpdates()
    {
        var canvas = new WMCanvas();
        canvas.Children.Add(new WMContainer { ID = "ROOT" });
        var state = WMTemplateEditorState.Create(canvas);
        var changes = new List<WMTemplateChangeSet>();
        state.DetailedChanged += changes.Add;

        state.BeginTransaction(
            "resize",
            WMTemplateChangeKind.Geometry,
            ["ROOT"]);
        state.Mutate(
            "resize",
            () => state.Draft.Children[0].Style.Transform.ScaleX = 2,
            WMTemplateChangeKind.Geometry,
            ["ROOT"]);
        Assert.True(state.CommitTransaction(WMTemplateChangeKind.Paint));

        Assert.Equal(WMTemplateChangeKind.Geometry, changes[1].Kind);
        Assert.Equal(
            WMTemplateChangeKind.Geometry | WMTemplateChangeKind.Paint,
            changes[2].Kind);
    }

    [Fact]
    public void SelectionChange_DoesNotReportPaintOrLayoutInvalidation()
    {
        var canvas = new WMCanvas();
        canvas.Children.Add(new WMContainer { ID = "ROOT" });
        var state = WMTemplateEditorState.Create(canvas);
        WMTemplateChangeSet? observed = null;
        state.DetailedChanged += change => observed = change;

        state.Select("ROOT");

        Assert.NotNull(observed);
        Assert.True(observed.IsSelectionOnly);
        Assert.Equal(["ROOT"], observed.NodeIds);
    }

    [Fact]
    public void RootContainerCoordinateTransform_IsUndoableAndRedoable()
    {
        var canvas = new WMCanvas();
        canvas.Children.Add(new WMContainer { ID = "ROOT" });
        var state = WMTemplateEditorState.Create(canvas);
        var root = Assert.Single(state.Draft.Children);

        state.BeginTransaction("canvas drag");
        MacCanvasTransform.Apply(root, new MacCanvasInteraction(root.ID, "drag", 12, -8, 1, 1, 0));
        Assert.True(state.CommitTransaction());
        Assert.Equal(WMPosition.Absolute, root.Style.Position);
        Assert.Equal(12, root.Style.Transform.OffsetXPercent);
        Assert.Equal(-8, root.Style.Transform.OffsetYPercent);

        Assert.True(state.Undo());
        var undone = Assert.Single(state.Draft.Children);
        Assert.Equal(0, undone.Style.Transform.OffsetXPercent);
        Assert.Equal(0, undone.Style.Transform.OffsetYPercent);

        Assert.True(state.Redo());
        var redone = Assert.Single(state.Draft.Children);
        Assert.Equal(12, redone.Style.Transform.OffsetXPercent);
        Assert.Equal(-8, redone.Style.Transform.OffsetYPercent);
    }

    [Fact]
    public void Create_PreservesRuntimeStateWithoutAliasingOriginal()
    {
        var original = RuntimeCanvas();
        var originalText = Assert.IsType<WMText>(WMControlTree.Find(original, "TEXT"));
        var state = WMTemplateEditorState.Create(original);
        var draftText = Assert.IsType<WMText>(WMControlTree.Find(state.Draft, "TEXT"));

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
        var state = WMTemplateEditorState.Create(original);
        var text = Assert.IsType<WMText>(WMControlTree.Find(state.Draft, "TEXT"));
        state.BeginTransaction("runtime");
        state.Draft.Path = "changed";
        state.Draft.Exif["camera"]["model"] = "changed-value";
        text.Width = 50;
        text.Height = 60;
        text.DesignX = 70;
        text.DesignY = 80;

        Assert.False(state.IsDirty);

        state.CancelTransaction();

        AssertRuntimeState(state.Draft, "original", "value", 10, 20, 30, 40);
        Assert.False(state.IsDirty);
        state.Draft.Exif["camera"]["model"] = "after-cancel";
        Assert.Equal("value", original.Exif["camera"]["model"]);
    }

    [Fact]
    public void RuntimeOnlyUndoRedo_RestoresPathExifAndGeometryWithoutDirtyState()
    {
        var state = WMTemplateEditorState.Create(RuntimeCanvas());
        state.BeginTransaction("runtime");
        var text = Assert.IsType<WMText>(WMControlTree.Find(state.Draft, "TEXT"));
        state.Draft.Path = "changed";
        state.Draft.Exif["camera"]["model"] = "changed-value";
        text.Width = 50;
        text.Height = 60;
        text.DesignX = 70;
        text.DesignY = 80;

        Assert.True(state.CommitTransaction());
        Assert.False(state.IsDirty);
        Assert.True(state.Undo());
        AssertRuntimeState(state.Draft, "original", "value", 10, 20, 30, 40);
        Assert.False(state.IsDirty);
        Assert.True(state.Redo());
        AssertRuntimeState(state.Draft, "changed", "changed-value", 50, 60, 70, 80);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public void PathAndExifOnlyTransaction_ParticipatesInHistoryWithoutDirtyState()
    {
        var state = WMTemplateEditorState.Create(RuntimeCanvas());
        state.BeginTransaction("runtime");
        state.Draft.Path = "changed";
        state.Draft.Exif["camera"]["model"] = "changed-value";

        Assert.True(state.CommitTransaction());
        Assert.Equal(2, state.HistoryCount);
        Assert.False(state.IsDirty);
        Assert.True(state.Undo());
        Assert.False(state.IsDirty);
        Assert.True(state.Redo());
        Assert.False(state.IsDirty);
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
        var text = Assert.IsType<WMText>(WMControlTree.Find(canvas, "TEXT"));
        Assert.Equal(path, canvas.Path);
        Assert.Equal(exif, canvas.Exif["camera"]["model"]);
        Assert.Equal(width, text.Width);
        Assert.Equal(height, text.Height);
        Assert.Equal(designX, text.DesignX);
        Assert.Equal(designY, text.DesignY);
    }
}
