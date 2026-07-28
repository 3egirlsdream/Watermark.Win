using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMPosterAssetSelectionTests
{
    [Fact]
    public void AssetSlot_RoundTripsWithV2ManifestAndNodeReference()
    {
        var canvas = CreateCanvas();
        var logo = Assert.IsType<WMLogo>(Assert.Single(canvas.Children));
        var slot = WMPosterAssetSlots.Ensure(canvas, logo, "主视觉");
        slot.Fit = WMPosterAssetFit.Contain;
        slot.SetCropSettings(
            WMCropSettings.Identity with
            {
                CenterX = 0.42,
                VisibleWidth = 0.6,
                VisibleHeight = 0.5,
                StraightenDegrees = 12,
                AspectPreset = WMCropAspectPreset.Free
            },
            1200,
            800);
        slot.IsRequired = true;
        slot.DefaultAssetId = "asset-a";

        var restored = Global.ReadConfig(Global.CanvasSerialize(canvas));
        var restoredLogo = Assert.IsType<WMLogo>(Assert.Single(restored.Children));
        var restoredSlot = Assert.Single(restored.PosterManifest.AssetSlots);

        Assert.Equal(slot.Id, restoredLogo.PosterMetadata?.AssetSlotId);
        Assert.Equal("主视觉", restoredSlot.Name);
        Assert.Equal(WMPosterAssetFit.Contain, restoredSlot.Fit);
        Assert.NotNull(restoredSlot.Crop.Settings);
        Assert.Equal(0.42, restoredSlot.Crop.Settings.CenterX, 6);
        Assert.Equal(0.6, restoredSlot.Crop.Settings.VisibleWidth, 6);
        Assert.Equal(12, restoredSlot.Crop.Settings.StraightenDegrees);
        Assert.True(restoredSlot.IsRequired);
        Assert.Equal("asset-a", restoredSlot.DefaultAssetId);
    }

    [Fact]
    public void Replacement_MultiplePreviewsCommitAsOneHistoryEntry()
    {
        var editor = WMTemplateEditorState.Create(CreateCanvas());
        var logo = Assert.IsType<WMLogo>(Assert.Single(editor.Draft.Children));
        var startHistory = editor.HistoryCount;
        var session = WMPosterAssetSelectionSession.Begin(editor, logo);

        session.Preview(Asset("asset-a", "/tmp/a.jpg"));
        session.Preview(Asset("asset-b", "/tmp/b.jpg"));
        session.PreviewFit(WMPosterAssetFit.Contain);
        session.PreviewCrop(WMCropSettings.Identity with
        {
            VisibleWidth = 0.75,
            VisibleHeight = 0.75,
            StraightenDegrees = -8,
            AspectPreset = WMCropAspectPreset.Square
        });

        Assert.True(session.Confirm());
        Assert.Equal(startHistory + 1, editor.HistoryCount);
        var replacedLogo = Assert.IsType<WMLogo>(Assert.Single(editor.Draft.Children));
        Assert.Equal("/tmp/b.jpg", replacedLogo.Path);
        Assert.False(replacedLogo.AutoSetLogo);
        var slot = Assert.Single(editor.Draft.PosterManifest.AssetSlots);
        Assert.Equal("asset-b", slot.DefaultAssetId);
        Assert.Equal(WMPosterAssetFit.Contain, slot.Fit);
        Assert.NotNull(slot.Crop.Settings);
        Assert.Equal(WMCropAspectPreset.Square, slot.Crop.Settings.AspectPreset);
        Assert.Equal(-8, slot.Crop.Settings.StraightenDegrees);

        Assert.True(editor.Undo());
        var restoredLogo = Assert.IsType<WMLogo>(Assert.Single(editor.Draft.Children));
        Assert.Equal("original.png", restoredLogo.Path);
        Assert.True(restoredLogo.AutoSetLogo);
        Assert.Empty(editor.Draft.PosterManifest.AssetSlots);
        Assert.Null(restoredLogo.PosterMetadata);
    }

    [Fact]
    public void Replacement_CancelRestoresPathSlotAndHistory()
    {
        var editor = WMTemplateEditorState.Create(CreateCanvas());
        var logo = Assert.IsType<WMLogo>(Assert.Single(editor.Draft.Children));
        var startHistory = editor.HistoryCount;
        var session = WMPosterAssetSelectionSession.Begin(editor, logo);

        session.Preview(Asset("asset-a", "/tmp/a.jpg"));
        session.PreviewFit(WMPosterAssetFit.Fill);
        session.PreviewCrop(WMCropSettings.Identity with
        {
            StraightenDegrees = 20,
            AspectPreset = WMCropAspectPreset.SixteenNine
        });
        session.Cancel();

        var restoredLogo = Assert.IsType<WMLogo>(Assert.Single(editor.Draft.Children));
        Assert.Equal("original.png", restoredLogo.Path);
        Assert.Empty(editor.Draft.PosterManifest.AssetSlots);
        Assert.Null(restoredLogo.PosterMetadata);
        Assert.Equal(startHistory, editor.HistoryCount);
        Assert.False(editor.IsTransactionActive);
    }

    [Fact]
    public void CropSettings_UseSharedPlannerBeforeTheyEnterTheTemplate()
    {
        var editor = WMTemplateEditorState.Create(CreateCanvas());
        var logo = Assert.IsType<WMLogo>(Assert.Single(editor.Draft.Children));
        var session = WMPosterAssetSelectionSession.Begin(editor, logo);
        session.Preview(Asset("asset-a", "/tmp/a.jpg"));

        var input = WMCropSettings.Identity with
        {
            CenterX = -10,
            VisibleWidth = 5,
            StraightenDegrees = -90,
            AspectPreset = WMCropAspectPreset.Free
        };
        session.PreviewCrop(input);
        var expected = WMCropPlanner.Normalize(input, 1200, 800);

        Assert.Equal(expected, session.CropSettings);
        session.Cancel();
    }

    [Fact]
    public void LegacyCropFields_MigrateIntoTheSharedCropModelOnTheNextEdit()
    {
        var slot = new WMPosterAssetSlot
        {
            Crop = new WMPosterAssetCrop
            {
                AspectRatio = 1,
                RotationDegrees = 15
            }
        };

        var migrated = slot.GetCropSettings(1200, 800);

        Assert.Null(slot.Crop.Settings);
        Assert.Equal(
            1,
            migrated.VisibleWidth * 1200 / (migrated.VisibleHeight * 800),
            6);
        Assert.Equal(15, migrated.StraightenDegrees);
        Assert.Equal(WMCropAspectPreset.Free, migrated.AspectPreset);

        slot.SetCropSettings(migrated, 1200, 800);

        Assert.NotNull(slot.Crop.Settings);
        Assert.Equal(0, slot.Crop.AspectRatio);
        Assert.Equal(0, slot.Crop.RotationDegrees);
    }

    [Fact]
    public void DuplicateAndRemove_KeepManifestReferencesUniqueAndPruned()
    {
        var canvas = CreateCanvas();
        var logo = Assert.IsType<WMLogo>(Assert.Single(canvas.Children));
        WMPosterAssetSlots.Ensure(canvas, logo);

        var copy = Assert.IsType<WMLogo>(WMControlTree.Duplicate(canvas, logo.ID));

        Assert.NotEqual(logo.PosterMetadata?.AssetSlotId, copy.PosterMetadata?.AssetSlotId);
        Assert.Equal(2, canvas.PosterManifest.AssetSlots.Count);

        Assert.True(WMControlTree.Remove(canvas, copy.ID));
        Assert.Single(canvas.PosterManifest.AssetSlots);
        Assert.Equal(
            logo.PosterMetadata?.AssetSlotId,
            canvas.PosterManifest.AssetSlots[0].Id);
    }

    private static WMCanvas CreateCanvas()
    {
        var canvas = new WMCanvas
        {
            ID = "POSTER",
            Name = "海报",
            LayoutSchemaVersion = WMLayoutMigration.CurrentSchemaVersion
        };
        var logo = new WMLogo
        {
            ID = "LOGO",
            Name = "主图片",
            Path = "original.png",
            AutoSetLogo = true,
            PNode = new WMPNode(0, "0")
        };
        logo.Style.Position = WMPosition.Absolute;
        canvas.Children.Add(logo);
        return canvas;
    }

    private static WMPosterAssetReference Asset(string id, string path) => new(
        id,
        WMPosterAssetSource.Device,
        "image/jpeg",
        id,
        path,
        string.Empty,
        10,
        1200,
        800);
}
