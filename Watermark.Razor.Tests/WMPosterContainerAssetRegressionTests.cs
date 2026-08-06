using System.Collections;
using System.Reflection;
using SkiaSharp;
using Watermark.Razor.Components.Compatibility;
using Watermark.Razor.Components.Mac;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMPosterContainerAssetRegressionTests
{
    [Fact]
    public void AssetPurposeOptions_UseBundledPhosphorIcons()
    {
        var field = typeof(MacSelectionInspector).GetField(
            "AssetPurposeOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        var options = Assert.IsAssignableFrom<IEnumerable>(field?.GetValue(null));
        var icons = options.Cast<object>()
            .Select(option => Assert.IsType<string>(
                option.GetType().GetProperty("Icon")?.GetValue(option)))
            .ToArray();

        Assert.Contains("shapes", icons);
        Assert.All(
            icons,
            icon => Assert.False(string.IsNullOrWhiteSpace(WmPhosphorIconPaths.Get(icon))));
    }

    [Fact]
    public async Task FixedBlankCanvas_PreviewsPngStoredWithJpegExtensionInNewRootContainer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"poster-asset-{Guid.NewGuid():N}.jpg");
        try
        {
            using (var bitmap = new SKBitmap(1086, 1448))
            {
                bitmap.Erase(SKColors.CornflowerBlue);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                await using var output = File.Create(path);
                data.SaveTo(output);
            }

            var canvas = new WMCanvas
            {
                ID = Guid.NewGuid().ToString("N"),
                Name = "blank fixed poster",
                LayoutSchemaVersion = WMLayoutMigration.CurrentSchemaVersion,
                CanvasSizing = new WMCanvasSizing
                {
                    Mode = WMCanvasSizingMode.Fixed,
                    ReferenceWidth = 4500,
                    ReferenceHeight = 6000
                }
            };
            WMPosterAssetSlots.SetPrimaryEnabled(canvas, true);
            var container = Assert.IsType<WMContainer>(
                WMControlTree.Add(canvas, typeof(WMContainer), null));
            var editor = WMTemplateEditorState.Create(canvas);
            container = Assert.IsType<WMContainer>(WMControlTree.Find(editor.Draft, container.ID));
            var session = WMPosterAssetSelectionSession.Begin(editor, container);
            session.Preview(new WMPosterAssetReference(
                "asset",
                WMPosterAssetSource.Device,
                "image/jpeg",
                "photo-1.jpg",
                path,
                string.Empty,
                new FileInfo(path).Length,
                1086,
                1448));

            var rendered = await new WatermarkHelper()
                .GenerationDesignPreviewAsync(editor.Draft, null);

            Assert.NotEmpty(rendered.ImageBytes);
            Assert.Contains(rendered.Bounds, item => item.ControlId == container.ID);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
