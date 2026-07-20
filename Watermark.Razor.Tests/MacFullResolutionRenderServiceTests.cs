using SkiaSharp;
using System.Text.Json;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMFullResolutionRenderPipelineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "watermark-full-render-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RenderAsync_ReplaysFromFullSizeBaseInsteadOfProxyPixels()
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "source.png");
        var proxyPath = Path.Combine(root, "proxy.png");
        WriteImage(sourcePath, 1800, 900, new SKColor(180, 90, 40));
        WriteImage(proxyPath, 160, 80, new SKColor(20, 30, 40));
        var source = new WMImageArtifact
        {
            Id = "source",
            FilePath = sourcePath,
            SourceOperation = WMImageOperationKind.Source,
            Width = 1800,
            Height = 900
        };
        var operation = WMImageOperation.Create(
            WMImageOperationKind.ColorGrade,
            [source.Id],
            ["proxy"],
            new WMColorRecipe());
        var plan = new WMRenderPlan(source, [new WMRenderPlanStep(operation)], source);
        var outputPath = Path.Combine(root, "output.jpg");
        var workingRoot = Path.Combine(root, "working");
        using var scheduler = new WMProcessingScheduler();
        var service = new WMFullResolutionRenderPipeline(
            new WMTemplateOperationProcessor(new WMTemplateRenderer(new WatermarkHelper()), scheduler),
            new WMColorGradeOperationProcessor(scheduler),
            scheduler);

        await service.RenderAsync(new WMFullResolutionRenderRequest(
            plan,
            outputPath,
            sourcePath,
            "default",
            90,
            workingRoot,
            new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 }));

        using var output = SKBitmap.Decode(outputPath);
        Assert.NotNull(output);
        Assert.Equal(1800, output.Width);
        Assert.Equal(900, output.Height);
        Assert.True(File.ReadAllBytes(outputPath).AsSpan().IndexOf("ICC_PROFILE\0"u8) >= 0);
        Assert.Empty(Directory.EnumerateFiles(workingRoot, "*.wm16", SearchOption.AllDirectories));
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(workingRoot, "fast-jpeg-cache"), "*.jpg", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task RenderAsync_ReplaysNonNeutralColorGradeIntoFinalPixels()
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "graded-source.png");
        WriteImage(sourcePath, 640, 360, new SKColor(70, 80, 90));
        var source = new WMImageArtifact
        {
            Id = "graded-source",
            FilePath = sourcePath,
            SourceOperation = WMImageOperationKind.Source,
            Width = 640,
            Height = 360,
            ContentHash = "graded-source-hash"
        };
        var recipe = new WMColorRecipe();
        recipe.Grade.Exposure = 1;
        recipe.Grade.Saturation = 35;
        var operation = WMImageOperation.Create(WMImageOperationKind.ColorGrade, [source.Id], ["proxy"], recipe);
        var outputPath = Path.Combine(root, "graded-output.jpg");
        using var scheduler = new WMProcessingScheduler();
        var service = CreateService(scheduler);

        await service.RenderAsync(new WMFullResolutionRenderRequest(
            new WMRenderPlan(source, [new WMRenderPlanStep(operation)], source),
            outputPath,
            sourcePath,
            "default",
            100,
            Path.Combine(root, "graded-working"),
            new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 }));

        using var output = SKBitmap.Decode(outputPath);
        var pixel = output.GetPixel(output.Width / 2, output.Height / 2);
        Assert.True(pixel.Red > 90, $"线性曝光调色未生效，红色通道为 {pixel.Red}");
        Assert.True(pixel.Green > 100, $"线性曝光调色未生效，绿色通道为 {pixel.Green}");
    }

    [Fact]
    public async Task RenderAsync_ReplaysAdaptiveReferenceLookAfterRecipeSerialization()
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "adaptive-source.png");
        var referencePath = Path.Combine(root, "adaptive-reference.png");
        WriteImage(sourcePath, 640, 360, new SKColor(55, 95, 165));
        WriteImage(referencePath, 320, 180, new SKColor(185, 105, 55));
        var source = new WMImageArtifact
        {
            Id = "adaptive-source",
            FilePath = sourcePath,
            SourceOperation = WMImageOperationKind.Source,
            Width = 640,
            Height = 360,
            ContentHash = "adaptive-source-hash"
        };
        var recipe = new WMColorRecipe
        {
            ReferenceProfile = ColorAnalyzer.AnalyzeProfile(referencePath),
            ReferenceMapping = new WMColorReferenceMappingSettings
            {
                IsConfigured = true,
                Enabled = true,
                Strength = 85
            }
        };
        using (var targetBitmap = SKBitmap.Decode(sourcePath))
        {
            var targetProfile = ColorAnalyzer.AnalyzeProfile(targetBitmap);
            targetProfile.SourceHash = source.ContentHash;
            var generated = new WMColorLookMapper().Map(new WMColorLookMappingRequest(
                recipe.ReferenceProfile,
                targetProfile,
                recipe.ReferenceMapping));
            using var direct = ColorGradeApplier.ApplyGeneratedLook(targetBitmap, generated, recipe.Grade);
            var directPixel = direct.GetPixel(direct.Width / 2, direct.Height / 2);
            Assert.True(directPixel.Red > 100, $"参考风格直接处理失败，红色通道为 {directPixel.Red}");
        }
        var operation = WMImageOperation.Create(WMImageOperationKind.ColorGrade, [source.Id], ["adaptive-proxy"], recipe);
        var restored = JsonSerializer.Deserialize<WMColorRecipe>(operation.ParametersJson)!;
        Assert.True(restored.ReferenceMapping.Enabled);
        Assert.True(restored.ReferenceMapping.IsConfigured);
        Assert.NotNull(restored.ReferenceProfile);
        using (var targetBitmap = SKBitmap.Decode(sourcePath))
        {
            var targetProfile = ColorAnalyzer.AnalyzeProfile(targetBitmap);
            targetProfile.SourceHash = source.ContentHash;
            var restoredLook = new WMColorLookMapper().Map(new WMColorLookMappingRequest(
                restored.ReferenceProfile!, targetProfile, restored.ReferenceMapping));
            using var restoredDirect = ColorGradeApplier.ApplyGeneratedLook(targetBitmap, restoredLook, restored.Grade);
            Assert.True(restoredDirect.GetPixel(320, 180).Red > 100, "序列化后的风格参数已丢失");
        }
        var outputPath = Path.Combine(root, "adaptive-output.jpg");
        using var scheduler = new WMProcessingScheduler();
        var processor = new WMColorGradeOperationProcessor(scheduler);
        var directResult = await processor.ExecuteAsync(new WMOperationRequest(
            [source], restored, false, Path.Combine(root, "adaptive-direct"),
            Execution: new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 }));
        using (var directOutput = SKBitmap.Decode(directResult.Outputs.Single().FilePath))
        {
            var processorPixel = directOutput.GetPixel(320, 180);
            Assert.True(processorPixel.Red > 100,
                $"序列化后的调色处理器未应用参考风格，输出={processorPixel}");
        }

        await CreateService(scheduler).RenderAsync(new WMFullResolutionRenderRequest(
            new WMRenderPlan(source, [new WMRenderPlanStep(operation)], source), outputPath, sourcePath, "default", 100,
            Path.Combine(root, "adaptive-working"),
            new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 }));

        using var output = SKBitmap.Decode(outputPath);
        var pixel = output.GetPixel(output.Width / 2, output.Height / 2);
        Assert.True(pixel.Red > 100, $"参考风格未重放，红色通道为 {pixel.Red}");
        Assert.True(pixel.Blue < 130, $"参考风格未重放，蓝色通道为 {pixel.Blue}");
    }

    [Fact]
    public async Task RenderAsync_WritesRealPng16FromHighPrecisionBase()
    {
        Directory.CreateDirectory(root);
        var proxyPath = Path.Combine(root, "high-proxy.png");
        WriteImage(proxyPath, 512, 1, new SKColor(128, 128, 128));
        var highPath = Path.Combine(root, "high-base.wm16");
        var samples = new ushort[512 * 4];
        for (var x = 0; x < 512; x++)
        {
            var value = (ushort)Math.Round(x / 511d * 65535);
            samples[x * 4] = samples[x * 4 + 1] = samples[x * 4 + 2] = value;
            samples[x * 4 + 3] = ushort.MaxValue;
        }
        string hash;
        using (var writer = new WM16FileWriter(highPath, 512, 1, 4, 16))
        {
            writer.WriteTile(new WMLinearTile(0, 1, 512, 4, samples));
            hash = writer.Complete();
        }
        var artifact = new WMImageArtifact
        {
            Id = "high-base",
            FilePath = proxyPath,
            SourceOperation = WMImageOperationKind.StarTrail,
            Width = 512,
            Height = 1,
            HighPrecision = new WMHighPrecisionArtifact
            {
                FilePath = highPath,
                Width = 512,
                Height = 1,
                ContentHash = hash
            }
        };
        var outputPath = Path.Combine(root, "high-output.png");
        using var scheduler = new WMProcessingScheduler();
        await CreateService(scheduler).RenderAsync(new WMFullResolutionRenderRequest(
            new WMRenderPlan(artifact, [], artifact),
            outputPath,
            proxyPath,
            "default",
            100,
            Path.Combine(root, "high-working"),
            new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 },
            WMExportFormat.Png16));

        Assert.True(WMPng16Encoder.IsPng16(outputPath));
    }

    [Fact]
    public async Task RenderAsync_CropReplaysAtHighPrecisionWithSharedOutputDimensions()
    {
        Directory.CreateDirectory(root);
        var proxyPath = Path.Combine(root, "crop-high-proxy.png");
        WriteImage(proxyPath, 200, 120, new SKColor(80, 130, 190));
        var highPath = Path.Combine(root, "crop-high-base.wm16");
        var samples = new ushort[200 * 120 * 4];
        for (var pixel = 0; pixel < 200 * 120; pixel++)
        {
            samples[pixel * 4] = 12_000;
            samples[pixel * 4 + 1] = 32_000;
            samples[pixel * 4 + 2] = 52_000;
            samples[pixel * 4 + 3] = ushort.MaxValue;
        }
        string hash;
        using (var writer = new WM16FileWriter(highPath, 200, 120, 4, 120))
        {
            writer.WriteTile(new WMLinearTile(0, 120, 200, 4, samples));
            hash = writer.Complete();
        }
        var artifact = new WMImageArtifact
        {
            Id = "crop-high-base",
            FilePath = proxyPath,
            SourceOperation = WMImageOperationKind.Source,
            Width = 200,
            Height = 120,
            HighPrecision = new WMHighPrecisionArtifact
            {
                FilePath = highPath,
                Width = 200,
                Height = 120,
                ContentHash = hash
            }
        };
        var crop = WMImageOperation.Create(
            WMImageOperationKind.Crop,
            [artifact.Id],
            ["crop-high-output"],
            new WMCropSettings
            {
                VisibleWidth = .5,
                VisibleHeight = .5,
                AspectPreset = WMCropAspectPreset.Free
            });
        var outputPath = Path.Combine(root, "crop-high-output.png");
        var metrics = new WMWorkspacePerformanceCounters();
        using var scheduler = new WMProcessingScheduler();
        var service = new WMFullResolutionRenderPipeline(
            new WMTemplateOperationProcessor(new WMTemplateRenderer(new WatermarkHelper()), scheduler),
            new WMColorGradeOperationProcessor(scheduler),
            scheduler,
            metrics: metrics);

        await service.RenderAsync(new WMFullResolutionRenderRequest(
            new WMRenderPlan(artifact, [new WMRenderPlanStep(crop)], artifact),
            outputPath,
            proxyPath,
            "default",
            100,
            Path.Combine(root, "crop-high-working"),
            new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 },
            WMExportFormat.Png16));

        Assert.True(WMPng16Encoder.IsPng16(outputPath));
        using var decoded = SKBitmap.Decode(outputPath);
        Assert.Equal(100, decoded.Width);
        Assert.Equal(60, decoded.Height);
        Assert.Equal(1, metrics.Snapshot().Calls[WMWorkspaceMetricStage.Crop]);
    }

    [Fact]
    public async Task RenderAsync_CropUsesSameDimensionsInFastJpegPipeline()
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "crop-fast-source.png");
        WriteImage(sourcePath, 200, 120, new SKColor(80, 130, 190));
        var artifact = new WMImageArtifact
        {
            Id = "crop-fast-source",
            FilePath = sourcePath,
            SourceOperation = WMImageOperationKind.Source,
            Width = 200,
            Height = 120,
            ContentHash = "crop-fast-source-hash"
        };
        var crop = WMImageOperation.Create(
            WMImageOperationKind.Crop,
            [artifact.Id],
            ["crop-fast-output"],
            new WMCropSettings
            {
                VisibleWidth = .5,
                VisibleHeight = .5,
                AspectPreset = WMCropAspectPreset.Free
            });
        var outputPath = Path.Combine(root, "crop-fast-output.jpg");
        var metrics = new WMWorkspacePerformanceCounters();
        using var scheduler = new WMProcessingScheduler();
        var service = new WMFullResolutionRenderPipeline(
            new WMTemplateOperationProcessor(new WMTemplateRenderer(new WatermarkHelper()), scheduler),
            new WMColorGradeOperationProcessor(scheduler),
            scheduler,
            metrics: metrics);

        await service.RenderAsync(new WMFullResolutionRenderRequest(
            new WMRenderPlan(artifact, [new WMRenderPlanStep(crop)], artifact),
            outputPath,
            sourcePath,
            "default",
            100,
            Path.Combine(root, "crop-fast-working"),
            new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 }));

        using var decoded = SKBitmap.Decode(outputPath);
        Assert.Equal(100, decoded.Width);
        Assert.Equal(60, decoded.Height);
        Assert.Equal(1, metrics.Snapshot().Calls[WMWorkspaceMetricStage.Crop]);
    }

    [Fact]
    public async Task RenderAsync_ExportsCommittedHighPrecisionVersionInsteadOfBaseOrProxy()
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "committed-source.png");
        var proxyPath = Path.Combine(root, "committed-proxy.png");
        WriteImage(sourcePath, 8, 8, SKColors.Red);
        WriteImage(proxyPath, 8, 8, SKColors.Blue);
        var source = new WMImageArtifact
        {
            Id = "committed-source", FilePath = sourcePath, Width = 8, Height = 8,
            SourceOperation = WMImageOperationKind.Source
        };
        var wm16Path = Path.Combine(root, "committed-current.wm16");
        var samples = new ushort[8 * 8 * 4];
        for (var pixel = 0; pixel < 64; pixel++)
        {
            samples[pixel * 4] = 0;
            samples[pixel * 4 + 1] = 50_000;
            samples[pixel * 4 + 2] = 0;
            samples[pixel * 4 + 3] = ushort.MaxValue;
        }
        string hash;
        using (var writer = new WM16FileWriter(wm16Path, 8, 8, 4, 8))
        {
            writer.WriteTile(new WMLinearTile(0, 8, 8, 4, samples));
            hash = writer.Complete();
        }
        var current = new WMImageArtifact
        {
            Id = "committed-current", FilePath = proxyPath, Width = 8, Height = 8,
            SourceOperation = WMImageOperationKind.ColorGrade,
            HighPrecision = new WMHighPrecisionArtifact
            {
                FilePath = wm16Path, Width = 8, Height = 8, ContentHash = hash
            }
        };
        var outputPath = Path.Combine(root, "committed-output.png");
        using var scheduler = new WMProcessingScheduler();

        await CreateService(scheduler).RenderAsync(new WMFullResolutionRenderRequest(
            new WMRenderPlan(source, [], current), outputPath, sourcePath, "default", 100,
            Path.Combine(root, "committed-working"),
            new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 },
            WMExportFormat.Png16));

        Assert.True(WMPng16Encoder.IsPng16(outputPath));
        using var decoded = SKBitmap.Decode(outputPath);
        var pixelColor = decoded.GetPixel(4, 4);
        Assert.True(pixelColor.Green > pixelColor.Red && pixelColor.Green > pixelColor.Blue);
    }

    [Theory]
    [InlineData("1080", 1920, 640)]
    [InlineData("2160", 3840, 1280)]
    public async Task RenderAsync_FitsStandardVideoBoundsWithoutUpscaling(string resolution, int expectedWidth, int expectedHeight)
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, $"wide-{resolution}.png");
        WriteImage(sourcePath, 4800, 1600, SKColors.SteelBlue);
        var source = new WMImageArtifact
        {
            Id = $"wide-{resolution}",
            FilePath = sourcePath,
            SourceOperation = WMImageOperationKind.Source,
            Width = 4800,
            Height = 1600
        };
        var outputPath = Path.Combine(root, $"wide-{resolution}.jpg");
        using var scheduler = new WMProcessingScheduler();
        var service = CreateService(scheduler);

        await service.RenderAsync(new WMFullResolutionRenderRequest(
            new WMRenderPlan(source, [], source), outputPath, sourcePath, resolution, 90,
            Path.Combine(root, $"working-{resolution}"),
            new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 }));

        using var output = SKBitmap.Decode(outputPath);
        Assert.Equal(expectedWidth, output.Width);
        Assert.Equal(expectedHeight, output.Height);
    }

    [Fact]
    public async Task RenderAsync_RepeatedJpegExportUsesFinalFileCacheWithoutRenderingAgain()
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "cache-source.png");
        WriteImage(sourcePath, 640, 360, SKColors.SteelBlue);
        var source = new WMImageArtifact
        {
            Id = "cache-source",
            FilePath = sourcePath,
            SourceOperation = WMImageOperationKind.Source,
            Width = 640,
            Height = 360,
            ContentHash = "cache-source-content"
        };
        var canvas = new WMCanvas();
        var operation = WMImageOperation.Create(
            WMImageOperationKind.Template,
            [source.Id],
            ["cache-template"],
            new WMTemplateOperationSettings(canvas));
        var plan = new WMRenderPlan(source, [new WMRenderPlanStep(operation)], source);
        var workingRoot = Path.Combine(root, "cache-working");
        var renderer = new CountingTemplateRenderer();
        using var scheduler = new WMProcessingScheduler();
        var colorProcessor = new WMColorGradeOperationProcessor(scheduler);
        var fastService = new WMFastJpegExportService(renderer, colorProcessor, scheduler);
        var service = new WMFullResolutionRenderPipeline(
            new WMTemplateOperationProcessor(renderer, scheduler),
            colorProcessor,
            scheduler,
            fastJpegExportService: fastService);

        await service.RenderAsync(new WMFullResolutionRenderRequest(
            plan, Path.Combine(root, "first.jpg"), sourcePath, "default", 90, workingRoot,
            new WMOperationExecutionOptions { MaxConcurrentImages = 2, MaxPixelWorkers = 1 }));
        await service.RenderAsync(new WMFullResolutionRenderRequest(
            plan, Path.Combine(root, "second.jpg"), sourcePath, "default", 90, workingRoot,
            new WMOperationExecutionOptions { MaxConcurrentImages = 2, MaxPixelWorkers = 1 }));

        Assert.Equal(1, renderer.BitmapRenderCount);
        Assert.Equal(File.ReadAllBytes(Path.Combine(root, "first.jpg")),
            File.ReadAllBytes(Path.Combine(root, "second.jpg")));
        Assert.Empty(Directory.EnumerateFiles(workingRoot, "*.wm16", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RenderAsync_ZeroLengthFinalCacheIsInvalidatedAndRenderedAgain()
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "invalid-cache-source.png");
        WriteImage(sourcePath, 320, 180, SKColors.SteelBlue);
        var source = new WMImageArtifact
        {
            Id = "invalid-cache-source",
            FilePath = sourcePath,
            SourceOperation = WMImageOperationKind.Source,
            Width = 320,
            Height = 180,
            ContentHash = "invalid-cache-source-content"
        };
        var operation = WMImageOperation.Create(
            WMImageOperationKind.Template,
            [source.Id],
            ["invalid-cache-template"],
            new WMTemplateOperationSettings(new WMCanvas()));
        var plan = new WMRenderPlan(source, [new WMRenderPlanStep(operation)], source);
        var workingRoot = Path.Combine(root, "invalid-cache-working");
        var renderer = new CountingTemplateRenderer();
        using var scheduler = new WMProcessingScheduler();
        var service = new WMFastJpegExportService(
            renderer,
            new WMColorGradeOperationProcessor(scheduler),
            scheduler);
        var request = new WMFullResolutionRenderRequest(
            plan, Path.Combine(root, "valid-first.jpg"), sourcePath, "default", 90, workingRoot,
            new WMOperationExecutionOptions { MaxConcurrentImages = 1, MaxPixelWorkers = 1 });

        await service.RenderAsync(request);
        var cachePath = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(workingRoot, "fast-jpeg-cache"), "*.jpg"));
        await File.WriteAllBytesAsync(cachePath, []);

        await service.RenderAsync(request with { OutputPath = Path.Combine(root, "valid-second.jpg") });

        Assert.Equal(2, renderer.BitmapRenderCount);
        Assert.True(new FileInfo(cachePath).Length > 0);
    }

    private static WMFullResolutionRenderPipeline CreateService(WMProcessingScheduler scheduler) => new(
        new WMTemplateOperationProcessor(new WMTemplateRenderer(new WatermarkHelper()), scheduler),
        new WMColorGradeOperationProcessor(scheduler),
        scheduler);

    private static void WriteImage(string path, int width, int height, SKColor color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private sealed class CountingTemplateRenderer : IWMTemplateRenderer
    {
        public int BitmapRenderCount { get; private set; }

        public Task<byte[]> RenderAsync(WMTemplateRenderRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public SKBitmap RenderBitmap(WMCanvas canvas, SKBitmap source, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BitmapRenderCount++;
            return source.Copy();
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }
}
