using SkiaSharp;
using Watermark.Shared.Enums;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacDesignPreviewFallbackTests
{
    [Fact]
    public async Task DesignPreview_UsesPlaceholderWhenNormalTemplateHasNoSourceImage()
    {
        var canvas = new WMCanvas
        {
            ID = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Name = "missing-preview-source"
        };

        var result = await new WatermarkHelper().GenerationDesignPreviewAsync(canvas, null);

        Assert.NotEmpty(result.ImageBytes);
        Assert.Equal(1080, result.CanvasWidth);
        Assert.Equal(720, result.CanvasHeight);
    }

    [Fact]
    public async Task TemplateLibraryDesignModePreviewWithoutDefaultImageReturnsDecodableBytes()
    {
        var canvas = new WMCanvas
        {
            ID = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Name = "missing-library-preview-source"
        };

        var bytes = await new WatermarkHelper().GenerationAsync(canvas, null, true, designMode: true);
        Assert.NotEmpty(bytes);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(1080, bitmap.Width);
        Assert.Equal(720, bitmap.Height);
    }

    [Fact]
    public async Task DesignPreview_MissingExplicitSourceReturnsReadableErrorInsteadOfCodecError()
    {
        var canvas = new WMCanvas
        {
            Path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jpg")
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WatermarkHelper().GenerationDesignPreviewAsync(canvas, null));

        Assert.Contains("无法读取预览图片", error.Message);
        Assert.DoesNotContain("codec", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DesignPreview_MissingRelativeContainerImageDoesNotThrowCodecError()
    {
        var canvas = new WMCanvas
        {
            CanvasType = CanvasType.Split,
            CustomWidth = 1000,
            CustomHeight = 800,
            ImageProperties = new WMImage { Show = false }
        };
        canvas.Children.Add(new WMContainer
        {
            Path = $"missing-{Guid.NewGuid():N}.png",
            WidthPercent = 50,
            HeightPercent = 25,
            BackgroundColor = "#FFFFFFFF"
        });

        var result = await new WatermarkHelper().GenerationDesignPreviewAsync(canvas, null);

        Assert.NotEmpty(result.ImageBytes);
    }

    [Fact]
    public void DesignMode_LeavesUnstyledContainerTransparent()
    {
        var canvas = new WMCanvas
        {
            CanvasType = CanvasType.Split,
            CustomWidth = 200,
            CustomHeight = 100,
            BackgroundColor = "#FFFFFFFF",
            ImageProperties = new WMImage { Show = false }
        };
        canvas.Children.Add(new WMContainer
        {
            WidthPercent = 100,
            HeightPercent = 100,
            ContainerAlignment = ContainerAlignment.Top
        });

        var helper = new WatermarkHelper();
        var normalBytes = helper.Generation(canvas, null, true, designMode: false);
        var designBytes = helper.Generation(canvas, null, true, designMode: true);

        Assert.Equal(normalBytes, designBytes);
    }
}
