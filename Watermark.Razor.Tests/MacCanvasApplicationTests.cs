using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMCanvasApplicationTests
{
    [Fact]
    public void CreateForImage_PreservesEditedLayoutAndUsesTargetImageRuntimeData()
    {
        var source = new WMCanvas { Name = "edited", Path = "template-default.jpg" };
        var container = new WMContainer { ID = "CONTAINER" };
        container.Controls.Add(new WMLogo
        {
            ID = "LOGO",
            Transform = new WMTransform { OffsetXPercent = 24, ScaleX = 1.4 }
        });
        source.Children.Add(container);

        var target = new WMTemplateList
        {
            Path = "/photos/target.jpg",
            Canvas = new WMCanvas()
        };
        target.Canvas.Exif["camera"] = new Dictionary<string, string> { ["Model"] = "X-T5" };

        var applied = WMCanvasApplication.CreateForImage(source, target);
        var root = Assert.IsType<WMContainer>(Assert.Single(applied.Children));
        var logo = Assert.IsType<WMLogo>(Assert.Single(root.Controls));

        Assert.Equal("edited", applied.Name);
        Assert.Equal(24, logo.Transform!.OffsetXPercent);
        Assert.Equal(1.4, logo.Transform.ScaleX);
        Assert.Equal(target.Path, applied.Path);
        Assert.Equal("X-T5", applied.Exif["camera"]["Model"]);

        applied.Exif["camera"]["Model"] = "changed";
        Assert.Equal("X-T5", target.Canvas.Exif["camera"]["Model"]);
    }

    [Fact]
    public void CreateForImage_UsesCommittedArtifactWithoutReplacingImportedSourcePath()
    {
        var target = new WMTemplateList
        {
            Path = "/photos/original.jpg",
            Canvas = new WMCanvas { Path = "/editing-session/color-proxy.png" }
        };

        var applied = WMCanvasApplication.CreateForImage(new WMCanvas(), target);

        Assert.Equal("/photos/original.jpg", target.Path);
        Assert.Equal("/editing-session/color-proxy.png", applied.Path);
    }
}
