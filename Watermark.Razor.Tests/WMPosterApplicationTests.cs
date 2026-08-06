using Newtonsoft.Json.Linq;
using SkiaSharp;
using Watermark.Razor.Workspace;
using Watermark.Shared.Enums;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMPosterApplicationTests
{
    [Fact]
    public void NewPosterSerialization_RoundTripsSizingPrimaryAndOrderedSlotsWithoutLegacyFields()
    {
        var canvas = FixedCanvas();
        var primary = WMPosterAssetSlots.EnsurePrimary(canvas);
        primary.Id = "PRIMARY";
        primary.DefaultAssetId = "default";
        canvas.PosterManifest.PrimaryAssetSlotId = primary.Id;
        var photoOwner = Root(new WMContainer { ID = "PHOTO", Path = "photo.jpg" });
        var graphicOwner = Root(new WMLogo { ID = "GRAPHIC", Path = "graphic.png" });
        canvas.Children.Add(photoOwner);
        canvas.Children.Add(graphicOwner);
        var photo = WMPosterAssetSlots.Ensure(canvas, photoOwner);
        photo.Id = "PHOTO-SLOT";
        photoOwner.PosterMetadata!.AssetSlotId = photo.Id;
        photo.Purpose = WMPosterAssetPurpose.Photo;
        var graphic = WMPosterAssetSlots.Ensure(canvas, graphicOwner);
        graphic.Id = "GRAPHIC-SLOT";
        graphicOwner.PosterMetadata!.AssetSlotId = graphic.Id;
        graphic.Purpose = WMPosterAssetPurpose.Graphic;
        canvas.ImageProperties.CoverPhoto = true;
        canvas.ImageProperties.CoverPhotoAspectRatio = 1.5;
        photoOwner.ContainerProperties.FixImage = true;

        var json = Global.CanvasSerialize(canvas);
        var restored = Global.ReadConfig(json);

        Assert.DoesNotContain("\"CanvasType\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CustomWidth\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CustomHeight\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"LengthWidthRatio\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"FixImage\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CoverPhoto\"", json, StringComparison.Ordinal);
        Assert.Equal(WMCanvasSizingMode.Fixed, restored.CanvasSizing.Mode);
        Assert.Equal((4200, 6000), (
            restored.CanvasSizing.ReferenceWidth,
            restored.CanvasSizing.ReferenceHeight));
        Assert.Equal("PRIMARY", restored.PosterManifest.PrimaryAssetSlotId);
        Assert.Equal(
            ["PRIMARY", "PHOTO-SLOT", "GRAPHIC-SLOT"],
            restored.PosterManifest.AssetSlots.Select(slot => slot.Id).ToArray());
        Assert.Equal(
            [WMPosterAssetPurpose.Photo, WMPosterAssetPurpose.Photo, WMPosterAssetPurpose.Graphic],
            restored.PosterManifest.AssetSlots.Select(slot => slot.Purpose).ToArray());
        Assert.All(
            restored.PosterManifest.AssetSlots,
            slot => Assert.Equal(["image/*"], slot.AcceptedMediaTypes));
    }

    [Fact]
    public void LegacyNormal_MigratesToFollowPrimaryAndMovesCoverCropToPrimarySlot()
    {
        var json = LegacyJson(
            new WMCanvas
            {
                ID = "legacy-normal",
                Name = "legacy",
                ImageProperties = new WMImage()
            },
            CanvasType.Normal,
            coverPhoto: true,
            coverAspectRatio: 1.25);

        var restored = Global.ReadConfig(json);
        var primary = Assert.IsType<WMPosterAssetSlot>(
            WMPosterAssetSlots.FindPrimary(restored));

        Assert.Equal(WMCanvasSizingMode.FollowPrimary, restored.CanvasSizing.Mode);
        Assert.Equal(WMPosterAssetPurpose.Photo, primary.Purpose);
        Assert.Equal("default", primary.DefaultAssetId);
        Assert.Equal(WMPosterAssetFit.Cover, primary.Fit);
        Assert.Equal(1.25, primary.Crop.AspectRatio, 3);
    }

    [Fact]
    public void LegacySplit_MigratesReplaceableContainersButLeavesFixImageAsPlainLayer()
    {
        var canvas = new WMCanvas { ID = "legacy-split", Name = "legacy split" };
        canvas.Children.Add(new WMContainer { ID = "replaceable", Path = "photo.jpg" });
        canvas.Children.Add(new WMContainer { ID = "fixed", Path = "sticker.png" });
        var json = LegacyJson(
            canvas,
            CanvasType.Split,
            customWidth: 1200,
            customHeight: 800,
            fixedControlId: "fixed");

        var restored = Global.ReadConfig(json);

        Assert.Equal(WMCanvasSizingMode.Fixed, restored.CanvasSizing.Mode);
        Assert.Equal((1200, 800), (
            restored.CanvasSizing.ReferenceWidth,
            restored.CanvasSizing.ReferenceHeight));
        var replaceable = Assert.IsType<WMContainer>(restored.Children[0]);
        var fixedLayer = Assert.IsType<WMContainer>(restored.Children[1]);
        Assert.Equal(
            WMPosterAssetPurpose.Photo,
            WMPosterAssetSlots.Find(restored, replaceable)?.Purpose);
        Assert.Null(WMPosterAssetSlots.Find(restored, fixedLayer));
    }

    [Fact]
    public void Planner_CoversTemplateFirstAndImagesFirstCardinality()
    {
        var noPhotos = FixedCanvas();
        Assert.Single(WMPosterApplicationPlanner.FromTemplateFirst(noPhotos, []).Outputs);

        var one = FixedCanvas();
        var primary = WMPosterAssetSlots.EnsurePrimary(one);
        var onePlan = WMPosterApplicationPlanner.FromTemplateFirst(one, ["a", "b"]);
        Assert.Equal(2, onePlan.Outputs.Count);
        Assert.All(onePlan.Outputs, output => Assert.Single(output.Bindings));

        var multi = FixedCanvas();
        var multiPrimary = WMPosterAssetSlots.EnsurePrimary(multi);
        var secondOwner = Root(new WMContainer { ID = "second" });
        multi.Children.Add(secondOwner);
        var second = WMPosterAssetSlots.Ensure(multi, secondOwner);
        second.Purpose = WMPosterAssetPurpose.Photo;
        var graphicOwner = Root(new WMLogo { ID = "graphic" });
        multi.Children.Add(graphicOwner);
        WMPosterAssetSlots.Ensure(multi, graphicOwner).Purpose = WMPosterAssetPurpose.Graphic;

        var composition = WMPosterApplicationPlanner.FromTemplateFirst(multi, ["a", "b"]);
        var output = Assert.Single(composition.Outputs);
        Assert.Equal([multiPrimary.Id, second.Id], output.Bindings.Select(binding => binding.SlotId));
        Assert.Throws<InvalidOperationException>(() =>
            WMPosterApplicationPlanner.FromTemplateFirst(multi, ["a", "b", "c"]));

        var imagesFirst = WMPosterApplicationPlanner.FromImagesFirst(multi, ["a", "b"]);
        Assert.Equal(2, imagesFirst.Outputs.Count);
        Assert.All(imagesFirst.Outputs, item =>
            Assert.Equal(multiPrimary.Id, Assert.Single(item.Bindings).SlotId));

        var withoutPrimary = FixedCanvas();
        var ordinaryOwner = Root(new WMContainer { ID = "ordinary" });
        withoutPrimary.Children.Add(ordinaryOwner);
        WMPosterAssetSlots.Ensure(withoutPrimary, ordinaryOwner).Purpose = WMPosterAssetPurpose.Photo;
        Assert.Empty(Assert.Single(
            WMPosterApplicationPlanner.FromImagesFirst(withoutPrimary, ["a", "b"]).Outputs).Bindings);
    }

    [Fact]
    public void RuntimeResolver_BindsPrimaryAndOrdinaryExifWithoutOverwritingGraphicDefaults()
    {
        var canvas = FixedCanvas();
        var primary = WMPosterAssetSlots.EnsurePrimary(canvas);
        var photoOwner = Root(new WMContainer { ID = "photo", Path = string.Empty });
        var graphicOwner = Root(new WMLogo { ID = "graphic", Path = "default-graphic.png" });
        canvas.Children.Add(photoOwner);
        canvas.Children.Add(graphicOwner);
        var photo = WMPosterAssetSlots.Ensure(canvas, photoOwner);
        photo.Purpose = WMPosterAssetPurpose.Photo;
        var graphic = WMPosterAssetSlots.Ensure(canvas, graphicOwner);
        graphic.Purpose = WMPosterAssetPurpose.Graphic;
        var mainArtifact = Artifact("main", "/workspace/main.jpg", "Main Camera");
        var slotArtifact = Artifact("slot", "/workspace/slot.jpg", "Slot Camera");
        var output = new WMPosterOutputPlan(
            "output",
            Global.CanvasSerialize(canvas),
            [
                new WMPosterAssetBinding(primary.Id, mainArtifact.Id),
                new WMPosterAssetBinding(photo.Id, slotArtifact.Id)
            ],
            "poster.png");

        var resolved = WMPosterRuntimeCanvasResolver.Resolve(
            output,
            new Dictionary<string, WMImageArtifact>
            {
                [mainArtifact.Id] = mainArtifact,
                [slotArtifact.Id] = slotArtifact
            });

        Assert.Equal(mainArtifact.FilePath, resolved.Path);
        Assert.Equal("Main Camera", resolved.Exif[resolved.ID]["Model"]);
        Assert.Equal(slotArtifact.FilePath, Assert.IsType<WMContainer>(resolved.Children[0]).Path);
        Assert.Equal("Slot Camera", resolved.Exif["photo"]["Model"]);
        Assert.Equal("default-graphic.png", Assert.IsType<WMLogo>(resolved.Children[1]).Path);
        Assert.False(resolved.Exif.ContainsKey("graphic"));
    }

    [Fact]
    public void Renderer_SupportsFixedCanvasWithoutPrimaryAndIndependentOuterFrameRatio()
    {
        var canvas = FixedCanvas();
        canvas.CanvasSizing.ReferenceWidth = 120;
        canvas.CanvasSizing.ReferenceHeight = 80;
        canvas.BackgroundColor = "#FFFFFFFF";
        var helper = new WatermarkHelper();

        var fixedBytes = helper.Generation(canvas, null, isPreview: false);
        using var fixedBitmap = SKBitmap.Decode(fixedBytes);
        Assert.Equal((120, 80), (fixedBitmap.Width, fixedBitmap.Height));

        canvas.FrameProperties.Enabled = true;
        canvas.FrameProperties.AspectRatio = new WMAspectRatio { Width = 1, Height = 1 };
        var framedBytes = helper.Generation(canvas, null, isPreview: false);
        using var framedBitmap = SKBitmap.Decode(framedBytes);
        Assert.Equal((120, 120), (framedBitmap.Width, framedBitmap.Height));
    }

    [Theory]
    [InlineData(160, 90)]
    [InlineData(90, 160)]
    [InlineData(100, 100)]
    public void Renderer_FollowPrimaryKeepsEachSourceAspect(int width, int height)
    {
        var canvas = new WMCanvas
        {
            CanvasSizing = new WMCanvasSizing { Mode = WMCanvasSizingMode.FollowPrimary }
        };
        WMPosterAssetSlots.EnsurePrimary(canvas).DefaultAssetId = "default";
        using var source = new SKBitmap(width, height);
        source.Erase(SKColors.CornflowerBlue);

        using var rendered = new WMTemplateRenderer(new WatermarkHelper())
            .RenderBitmap(canvas, source);

        Assert.Equal((width, height), (rendered.Width, rendered.Height));
    }

    [Fact]
    public async Task EmptyPhotoSlot_SkipsBitmapButKeepsContainerAndChildrenInLayout()
    {
        var canvas = FixedCanvas();
        canvas.CanvasSizing.ReferenceWidth = 300;
        canvas.CanvasSizing.ReferenceHeight = 200;
        var container = Root(new WMContainer
        {
            ID = "empty-photo",
            Path = string.Empty,
            BackgroundColor = "#00000000"
        });
        var text = new WMText
        {
            ID = "caption",
            FontSize = 4,
            Exifs = [new WMExifConfigInfo { Prefix = "Caption" }]
        };
        container.Controls.Add(text);
        canvas.Children.Add(container);
        WMPosterAssetSlots.Ensure(canvas, container).Purpose = WMPosterAssetPurpose.Photo;

        var result = await new WatermarkHelper()
            .GenerationDesignPreviewAsync(canvas, null);

        Assert.NotEmpty(result.ImageBytes);
        Assert.Contains(result.Bounds, bounds => bounds.ControlId == container.ID);
        Assert.Contains(result.Bounds, bounds => bounds.ControlId == text.ID);
    }

    private static WMCanvas FixedCanvas() => new()
    {
        ID = Guid.NewGuid().ToString("N"),
        Name = "poster",
        LayoutSchemaVersion = WMLayoutMigration.CurrentSchemaVersion,
        CanvasSizing = new WMCanvasSizing
        {
            Mode = WMCanvasSizingMode.Fixed,
            ReferenceWidth = 4200,
            ReferenceHeight = 6000
        }
    };

    private static WMImageArtifact Artifact(
        string id,
        string path,
        string model) => new()
    {
        Id = id,
        FilePath = path,
        PreviewPath = path,
        ContentHash = id,
        Width = 100,
        Height = 80,
        Exif = new Dictionary<string, string> { ["Model"] = model }
    };

    private static T Root<T>(T control) where T : IWMControl
    {
        control.Style = new WMStyle
        {
            Position = WMPosition.Absolute,
            Left = WMStyleLength.Percent(0),
            Top = WMStyleLength.Percent(0),
            Width = WMStyleLength.Percent(50),
            Height = WMStyleLength.Percent(50)
        };
        return control;
    }

    private static string LegacyJson(
        WMCanvas canvas,
        CanvasType type,
        int customWidth = 0,
        int customHeight = 0,
        bool coverPhoto = false,
        double coverAspectRatio = 0,
        string? fixedControlId = null)
    {
        var json = JObject.Parse(Global.CanvasSerialize(canvas));
        json.Remove("CanvasSizing");
        json.Remove("PosterManifest");
        json["CanvasType"] = (int)type;
        json["CustomWidth"] = customWidth;
        json["CustomHeight"] = customHeight;
        json["LengthWidthRatio"] = "3:2";
        if (json["ImageProperties"] is JObject image)
        {
            image["CoverPhoto"] = coverPhoto;
            image["CoverPhotoAspectRatio"] = coverAspectRatio;
        }
        if (fixedControlId is not null
            && json["Containers"] is JArray containers
            && containers.OfType<JObject>().FirstOrDefault(child =>
                string.Equals((string?)child["ID"], fixedControlId, StringComparison.Ordinal))
            is { } fixedControl
            && fixedControl["ContainerProperties"] is JObject properties)
        {
            properties["FixImage"] = true;
        }
        return json.ToString();
    }
}
