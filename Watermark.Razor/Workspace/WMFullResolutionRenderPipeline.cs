#nullable enable

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed record WMFullResolutionRenderRequest(
    WMRenderPlan Plan,
    string OutputPath,
    string SourceMetadataPath,
    string Resolution,
    int Quality,
    string WorkingRoot,
    WMOperationExecutionOptions Execution,
    WMExportFormat Format = WMExportFormat.Jpeg8);

public sealed class WMFastJpegExportService
{
    public const int PipelineVersion = 2;

    private readonly IWMTemplateRenderer templateRenderer;
    private readonly WMColorGradeOperationProcessor colorProcessor;
    private readonly IWMProcessingScheduler scheduler;
    private readonly IWMWorkspacePerformanceCounters metrics;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> cacheLocks = new(StringComparer.Ordinal);

    public WMFastJpegExportService(
        IWMTemplateRenderer templateRenderer,
        WMColorGradeOperationProcessor colorProcessor,
        IWMProcessingScheduler scheduler,
        IWMWorkspacePerformanceCounters? metrics = null)
    {
        this.templateRenderer = templateRenderer;
        this.colorProcessor = colorProcessor;
        this.scheduler = scheduler;
        this.metrics = metrics ?? new WMWorkspacePerformanceCounters();
    }

    public async Task RenderAsync(
        WMFullResolutionRenderRequest request,
        IProgress<WMOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request.Format != WMExportFormat.Jpeg8)
            throw new ArgumentException("快速导出仅支持 JPEG8。", nameof(request));

        var cacheDirectory = Path.Combine(request.WorkingRoot, "fast-jpeg-cache");
        Directory.CreateDirectory(cacheDirectory);
        var cacheKey = CreateCacheKey(request);
        var cachePath = Path.Combine(cacheDirectory, $"{cacheKey}.jpg");
        var cacheLock = cacheLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
            {
                File.Copy(cachePath, request.OutputPath, true);
                progress?.Report(new WMOperationProgress(1, 1, "已复用会话导出缓存", WMOperationStage.Completed));
                return;
            }
            TryDelete(cachePath);

            var estimatedSize = GetTargetSize(
                request.Plan.BaseArtifact.Width,
                request.Plan.BaseArtifact.Height,
                request.Resolution);
            var estimatedMemory = Math.Max(
                64L * 1024 * 1024,
                (long)Math.Max(1, estimatedSize.Width) * Math.Max(1, estimatedSize.Height) * 24L);
            var bytes = await scheduler.RunAsync(parallelOptions =>
                RenderCore(request, progress, parallelOptions, cancellationToken),
                request.Execution,
                estimatedMemory,
                cancellationToken);

            var temporaryCache = cachePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryCache, bytes, cancellationToken);
                File.Move(temporaryCache, cachePath, true);
            }
            finally
            {
                TryDelete(temporaryCache);
            }
            File.Copy(cachePath, request.OutputPath, true);
            progress?.Report(new WMOperationProgress(1, 1, "JPEG 快速导出完成", WMOperationStage.Completed));
        }
        catch
        {
            TryDelete(request.OutputPath);
            throw;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private byte[] RenderCore(
        WMFullResolutionRenderRequest request,
        IProgress<WMOperationProgress>? progress,
        ParallelOptions parallelOptions,
        CancellationToken cancellationToken)
    {
        progress?.Report(new WMOperationProgress(0, 1, "正在解码 JPEG 快速导出源图…",
            WMOperationStage.Decoding, ItemPercentage: 5));
        using var codec = SKCodec.Create(request.Plan.BaseArtifact.FilePath)
            ?? throw new InvalidOperationException("无法读取导出源图。");
        using var decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException("无法解码导出源图。");
        var oriented = WatermarkHelper.AutoOrient(codec, decoded);
        using var orientedOwner = ReferenceEquals(oriented, decoded) ? null : oriented;
        using var srgb = WMImageBitmap.NormalizeToSrgb(oriented);
        var firstCrop = request.Plan.Steps.FirstOrDefault()?.Operation.Kind == WMImageOperationKind.Crop
            ? request.Plan.Steps[0].Operation
            : null;
        SKBitmap current;
        var firstReplayIndex = 0;
        if (firstCrop is not null)
        {
            var cropSettings = (WMCropSettings)WMFullResolutionRenderPipeline.DeserializeSettings(firstCrop);
            var nativePlan = WMCropPlanner.CreatePlan(cropSettings, srgb.Width, srgb.Height);
            var cropTarget = GetTargetSize(nativePlan.OutputWidth, nativePlan.OutputHeight, request.Resolution);
            using (metrics.Measure(WMWorkspaceMetricStage.Crop))
                current = WMCropProcessor.Apply(srgb, cropSettings, Math.Max(cropTarget.Width, cropTarget.Height));
            firstReplayIndex = 1;
        }
        else
        {
            var target = GetTargetSize(srgb.Width, srgb.Height, request.Resolution);
            current = ResizeIfNeeded(srgb, target.Width, target.Height);
        }
        try
        {
            for (var index = firstReplayIndex; index < request.Plan.Steps.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                parallelOptions.CancellationToken.ThrowIfCancellationRequested();
                var operation = request.Plan.Steps[index].Operation;
                progress?.Report(new WMOperationProgress(index, request.Plan.Steps.Count + 1,
                    $"正在重放编辑 {index + 1}/{request.Plan.Steps.Count}",
                    WMOperationStage.Processing, ItemPercentage: 15 + 65d * index / Math.Max(1, request.Plan.Steps.Count)));
                SKBitmap next;
                if (operation.Kind == WMImageOperationKind.Crop)
                {
                    var settings = (WMCropSettings)WMFullResolutionRenderPipeline.DeserializeSettings(operation);
                    using (metrics.Measure(WMWorkspaceMetricStage.Crop))
                        next = WMCropProcessor.Apply(current, settings);
                }
                else if (operation.Kind == WMImageOperationKind.ColorGrade)
                {
                    var recipe = JsonSerializer.Deserialize<WMColorRecipe>(operation.ParametersJson)
                        ?? throw new InvalidOperationException("无法恢复调色参数。");
                    next = colorProcessor.ApplyToBitmap(
                        current,
                        request.Plan.BaseArtifact,
                        recipe,
                        parallelOptions);
                }
                else if (operation.Kind == WMImageOperationKind.Template)
                {
                    var settings = (WMTemplateOperationSettings)WMFullResolutionRenderPipeline.DeserializeSettings(
                        operation, request.Plan.BaseArtifact);
                    next = templateRenderer.RenderBitmap(settings.Canvas, current, cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException($"JPEG 快速导出不支持操作 {operation.Kind}。");
                }
                current.Dispose();
                current = next;
            }

            var finalTarget = GetTargetSize(current.Width, current.Height, request.Resolution);
            if (finalTarget.Width != current.Width || finalTarget.Height != current.Height)
            {
                var resized = ResizeIfNeeded(current, finalTarget.Width, finalTarget.Height);
                current.Dispose();
                current = resized;
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new WMOperationProgress(request.Plan.Steps.Count, request.Plan.Steps.Count + 1,
                "正在编码 JPEG…", WMOperationStage.Encoding, ItemPercentage: 90));
            using var image = SKImage.FromBitmap(current);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, request.Quality)
                ?? throw new InvalidOperationException("无法编码 JPEG 导出结果。");
            var jpeg = JpegMetadataHelper.PreserveExif(
                data.ToArray(), request.SourceMetadataPath, current.Width, current.Height);
            return JpegMetadataHelper.AddSrgbIccProfile(jpeg);
        }
        finally
        {
            current.Dispose();
        }
    }

    private static (int Width, int Height) GetTargetSize(int width, int height, string resolution)
    {
        if (resolution == "default") return (Math.Max(1, width), Math.Max(1, height));
        if (resolution.StartsWith("max:", StringComparison.Ordinal)
            && int.TryParse(resolution.AsSpan(4), out var maximumEdge))
        {
            maximumEdge = Math.Clamp(maximumEdge, 320, 16384);
            var customScale = Math.Min(1f, maximumEdge / (float)Math.Max(1, Math.Max(width, height)));
            return (
                Math.Max(1, (int)Math.Round(width * customScale)),
                Math.Max(1, (int)Math.Round(height * customScale)));
        }
        var bounds = resolution == "1080" ? (Width: 1920, Height: 1080) : (Width: 3840, Height: 2160);
        var scale = Math.Min(1f, Math.Min(bounds.Width / (float)Math.Max(1, width), bounds.Height / (float)Math.Max(1, height)));
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static SKBitmap ResizeIfNeeded(SKBitmap source, int width, int height)
    {
        if (source.Width == width && source.Height == height) return source.Copy();
        return source.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()),
                   SKFilterQuality.Medium)
               ?? throw new InvalidOperationException("无法缩放 JPEG 快速导出图像。");
    }

    private static string CreateCacheKey(WMFullResolutionRenderRequest request)
    {
        var source = request.Plan.BaseArtifact.SourceFingerprint?.StableId
                     ?? request.Plan.BaseArtifact.ContentHash
                     ?? request.Plan.BaseArtifact.Id;
        var metadata = File.Exists(request.SourceMetadataPath)
            ? new FileInfo(request.SourceMetadataPath)
            : null;
        var builder = new StringBuilder()
            .Append("wm-fast-jpeg-v").Append(PipelineVersion).Append('|')
            .Append(source).Append('|')
            .Append(request.Resolution).Append('|')
            .Append(request.Quality).Append('|')
            .Append(metadata?.Length ?? 0).Append('|')
            .Append(metadata?.LastWriteTimeUtc.Ticks ?? 0);
        foreach (var step in request.Plan.Steps)
            builder.Append('|').Append((int)step.Operation.Kind).Append(':').Append(step.Operation.ParametersJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

public sealed class WMFullResolutionRenderPipeline
{
    private readonly IWMProcessingScheduler scheduler;
    private readonly WMHighPrecisionColorPipeline highPrecisionColorPipeline;
    private readonly IWMHighPrecisionTemplateRenderer highPrecisionTemplateRenderer;
    private readonly IWMTiff16Encoder tiff16Encoder;
    private readonly WMFastJpegExportService fastJpegExportService;
    private readonly IWMWorkspacePerformanceCounters metrics;

    public WMFullResolutionRenderPipeline(
        WMTemplateOperationProcessor templateProcessor,
        WMColorGradeOperationProcessor colorProcessor,
        IWMProcessingScheduler scheduler,
        WMHighPrecisionColorPipeline? highPrecisionColorPipeline = null,
        IWMHighPrecisionTemplateRenderer? highPrecisionTemplateRenderer = null,
        IWMTiff16Encoder? tiff16Encoder = null,
        WMFastJpegExportService? fastJpegExportService = null,
        IWMWorkspacePerformanceCounters? metrics = null)
    {
        this.scheduler = scheduler;
        this.highPrecisionColorPipeline = highPrecisionColorPipeline ?? new WMHighPrecisionColorPipeline();
        this.highPrecisionTemplateRenderer = highPrecisionTemplateRenderer
            ?? new WMHighPrecisionTemplateRenderer(new WatermarkHelper());
        this.tiff16Encoder = tiff16Encoder ?? new WMUnsupportedTiff16Encoder();
        this.metrics = metrics ?? new WMWorkspacePerformanceCounters();
        this.fastJpegExportService = fastJpegExportService
            ?? new WMFastJpegExportService(
                new WMTemplateRenderer(new WatermarkHelper()), colorProcessor, scheduler, this.metrics);
    }

    public async Task RenderAsync(
        WMFullResolutionRenderRequest request,
        IProgress<WMOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var highPrecisionPath = request.Plan.BaseArtifact.HighPrecision?.FilePath;
        if (request.Format == WMExportFormat.Jpeg8
            && (string.IsNullOrWhiteSpace(highPrecisionPath) || !File.Exists(highPrecisionPath)))
        {
            await fastJpegExportService.RenderAsync(request, progress, cancellationToken);
            return;
        }
        var renderDirectory = Path.Combine(request.WorkingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(renderDirectory);
        if (CanRenderHighPrecision(request))
        {
            await RenderHighPrecisionAsync(request, renderDirectory, progress, cancellationToken);
            return;
        }
        throw new InvalidOperationException("当前历史包含旧版调色或模板操作，无法保证16位输出真实有效；请重新应用调色后再导出。");
    }

    private bool CanRenderHighPrecision(WMFullResolutionRenderRequest request)
    {
        // A committed WM16 artifact is the authoritative current version. Its pixels already
        // contain every operation in user order, so exporting it must not replay or flatten a proxy.
        if (request.Plan.HasCommittedHighPrecision) return true;
        if (request.Plan.BaseArtifact.SourceOperation is WMImageOperationKind.StarTrail or WMImageOperationKind.MultiFrameStack
            && (request.Plan.BaseArtifact.HighPrecision is null
                || !File.Exists(request.Plan.BaseArtifact.HighPrecision.FilePath))
            && request.Plan.Steps.Count == 0
            && request.Format == WMExportFormat.Jpeg8)
            return false;
        foreach (var step in request.Plan.Steps)
        {
            if (step.Operation.Kind == WMImageOperationKind.Crop)
            {
                try { _ = DeserializeSettings(step.Operation); }
                catch { return false; }
                continue;
            }
            if (step.Operation.Kind == WMImageOperationKind.Template)
            {
                try { _ = DeserializeSettings(step.Operation); }
                catch { return false; }
                continue;
            }
            if (step.Operation.Kind == WMImageOperationKind.ColorGrade)
            {
                var recipe = JsonSerializer.Deserialize<WMColorRecipe>(step.Operation.ParametersJson);
                if (recipe is null) return false;
                recipe.UpgradeToCurrentSchema();
                if (recipe.PipelineVersion != WMColorPipelineVersion.OcioLinearSrgbV1) return false;
                continue;
            }
            return false;
        }
        return true;
    }

    private async Task RenderHighPrecisionAsync(
        WMFullResolutionRenderRequest request,
        string renderDirectory,
        IProgress<WMOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var succeeded = false;
        try
        {
            var committedPath = request.Plan.CurrentArtifact.HighPrecision?.FilePath;
            var useCommittedVersion = request.Plan.HasCommittedHighPrecision
                                      && !string.IsNullOrWhiteSpace(committedPath)
                                      && File.Exists(committedPath);
            var basePath = useCommittedVersion ? committedPath : request.Plan.BaseArtifact.HighPrecision?.FilePath;
            if (string.IsNullOrWhiteSpace(basePath) || !File.Exists(basePath))
            {
                basePath = Path.Combine(renderDirectory, "base.wm16");
                progress?.Report(new WMOperationProgress(0, request.Plan.Steps.Count + 1,
                    "正在解码16位线性素材…", WMOperationStage.Decoding, ItemPercentage: 5));
                var sourcePath = request.Plan.BaseArtifact.SourceOperation is WMImageOperationKind.StarTrail or WMImageOperationKind.MultiFrameStack
                    ? throw new InvalidOperationException("多帧高精度缓存缺失，不能从8位代理恢复；请重新合成。")
                    : request.Plan.BaseArtifact.FilePath;
                await scheduler.RunAsync(_ => WMHighPrecisionImage.DecodeToWm16(
                    sourcePath, basePath, CalculateTileHeight(request.Plan.BaseArtifact.Width, request.Execution.MemoryBudgetBytes),
                    cancellationToken), request.Execution, Math.Max(128L * 1024 * 1024,
                    (long)Math.Max(1, request.Plan.BaseArtifact.Width) * Math.Max(1, request.Plan.BaseArtifact.Height) * 16), cancellationToken);
            }

            var currentPath = basePath;
            var replaySteps = useCommittedVersion ? Array.Empty<WMRenderPlanStep>() : request.Plan.Steps;
            for (var index = 0; index < replaySteps.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = replaySteps[index].Operation;
                var output = Path.Combine(renderDirectory, $"step-{index + 1}.wm16");
                var stage = index;
                progress?.Report(new WMOperationProgress(stage, request.Plan.Steps.Count + 1,
                    $"正在以16位精度重放编辑 {index + 1}/{replaySteps.Count}…",
                    WMOperationStage.Processing, ItemPercentage: 20));
                if (operation.Kind == WMImageOperationKind.Crop)
                {
                    var settings = (WMCropSettings)DeserializeSettings(operation);
                    var cropMaximumEdge = ResolveCropMaximumLongEdge(
                        currentPath, settings, request.Resolution);
                    using (metrics.Measure(WMWorkspaceMetricStage.Crop))
                    {
                        currentPath = (await scheduler.RunAsync(_ => WMCropProcessor.ApplyHighPrecision(
                            currentPath,
                            output,
                            settings,
                            cropMaximumEdge,
                            cancellationToken), request.Execution,
                            EstimateHighPrecisionMemory(request.Plan.BaseArtifact, request.Execution),
                            cancellationToken)).FilePath;
                    }
                }
                else if (operation.Kind == WMImageOperationKind.ColorGrade)
                {
                    var recipe = JsonSerializer.Deserialize<WMColorRecipe>(operation.ParametersJson)
                        ?? throw new InvalidOperationException("无法恢复高精度调色参数。");
                    recipe.UpgradeToCurrentSchema();
                    currentPath = (await scheduler.RunAsync(parallelOptions => highPrecisionColorPipeline.Apply(
                        currentPath, output, recipe, parallelOptions, cancellationToken), request.Execution,
                        EstimateHighPrecisionMemory(request.Plan.BaseArtifact, request.Execution), cancellationToken)).FilePath;
                }
                else if (operation.Kind == WMImageOperationKind.Template)
                {
                    var settings = (WMTemplateOperationSettings)DeserializeSettings(
                        operation, request.Plan.BaseArtifact);
                    currentPath = (await scheduler.RunAsync(_ => highPrecisionTemplateRenderer.Render(
                        currentPath, output, settings.Canvas, cancellationToken), request.Execution,
                        EstimateHighPrecisionMemory(request.Plan.BaseArtifact, request.Execution), cancellationToken)).FilePath;
                }
                else
                {
                    throw new InvalidOperationException($"高精度导出重放不支持操作 {operation.Kind}。");
                }
            }

            currentPath = ResizeHighPrecisionIfNeeded(currentPath, request.Resolution, renderDirectory, cancellationToken);
            progress?.Report(new WMOperationProgress(request.Plan.Steps.Count, request.Plan.Steps.Count + 1,
                request.Format switch { WMExportFormat.Png16 => "正在编码16位PNG…", WMExportFormat.Tiff16 => "正在编码16位TIFF…", _ => "正在编码JPEG…" },
                WMOperationStage.Encoding, ItemPercentage: 80));
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            if (request.Format == WMExportFormat.Png16)
            {
                using var reader = new WM16FileReader(currentPath);
                WMPng16Encoder.Encode(reader, request.OutputPath, request.SourceMetadataPath, cancellationToken);
                if (!WMPng16Encoder.IsPng16(request.OutputPath))
                    throw new InvalidDataException("PNG编码结果不是16位，已阻止错误文件输出。");
            }
            else if (request.Format == WMExportFormat.Tiff16)
            {
                if (!tiff16Encoder.IsAvailable)
                    throw new PlatformNotSupportedException("当前发布包未加载 LibTIFF 后端，无法导出 TIFF16。" );
                using var reader = new WM16FileReader(currentPath);
                await tiff16Encoder.EncodeAsync(reader, request.OutputPath,
                    request.Plan.CurrentArtifact.Metadata ?? request.Plan.BaseArtifact.Metadata, cancellationToken);
            }
            else
            {
                using var reader = new WM16FileReader(currentPath);
                using var bitmap = WMHighPrecisionImage.ToSrgbBitmap(reader, dither: true, cancellationToken);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, request.Quality)
                    ?? throw new InvalidOperationException("无法编码高精度JPEG导出结果。");
                var jpeg = JpegMetadataHelper.PreserveExif(data.ToArray(), request.SourceMetadataPath, bitmap.Width, bitmap.Height);
                jpeg = JpegMetadataHelper.AddSrgbIccProfile(jpeg);
                await File.WriteAllBytesAsync(request.OutputPath, jpeg, cancellationToken);
            }
            succeeded = true;
            progress?.Report(new WMOperationProgress(request.Plan.Steps.Count + 1, request.Plan.Steps.Count + 1,
                "导出完成", WMOperationStage.Completed));
        }
        finally
        {
            TryDeleteDirectory(renderDirectory);
            if (!succeeded) TryDelete(request.OutputPath);
        }
    }

    private static string ResizeHighPrecisionIfNeeded(string currentPath, string resolution, string renderDirectory,
        CancellationToken cancellationToken)
    {
        if (resolution == "default") return currentPath;
        using var reader = new WM16FileReader(currentPath);
        float scale;
        if (resolution.StartsWith("max:", StringComparison.Ordinal)
            && int.TryParse(resolution.AsSpan(4), out var maximumEdge))
        {
            maximumEdge = Math.Clamp(maximumEdge, 320, 16384);
            scale = Math.Min(1f, maximumEdge / (float)Math.Max(reader.Width, reader.Height));
        }
        else
        {
            var bounds = resolution == "1080" ? (Width: 1920, Height: 1080) : (Width: 3840, Height: 2160);
            scale = Math.Min(1f, Math.Min(bounds.Width / (float)reader.Width, bounds.Height / (float)reader.Height));
        }
        if (scale >= 0.9999f) return currentPath;
        var width = Math.Max(1, (int)Math.Round(reader.Width * scale));
        var height = Math.Max(1, (int)Math.Round(reader.Height * scale));
        var output = Path.Combine(renderDirectory, $"resized-{width}x{height}.wm16");
        WMHighPrecisionImage.Resize(currentPath, output, width, height, cancellationToken);
        return output;
    }

    private static int? ResolveCropMaximumLongEdge(
        string currentPath,
        WMCropSettings settings,
        string resolution)
    {
        if (resolution == "default") return null;
        using var reader = new WM16FileReader(currentPath);
        var plan = WMCropPlanner.CreatePlan(settings, reader.Width, reader.Height);
        if (resolution.StartsWith("max:", StringComparison.Ordinal)
            && int.TryParse(resolution.AsSpan(4), out var maximumEdge))
            return Math.Clamp(maximumEdge, 320, 16384);
        var bounds = resolution == "1080" ? (Width: 1920, Height: 1080) : (Width: 3840, Height: 2160);
        var scale = Math.Min(1d, Math.Min(
            bounds.Width / (double)plan.OutputWidth,
            bounds.Height / (double)plan.OutputHeight));
        return Math.Max(1, (int)Math.Round(Math.Max(plan.OutputWidth, plan.OutputHeight) * scale));
    }

    private static int CalculateTileHeight(int width, long memoryBudgetBytes) =>
        (int)Math.Clamp(Math.Max(8L * 1024 * 1024, memoryBudgetBytes / 16)
            / Math.Max(1L, width * 4L * sizeof(ushort) * 3L), 32, 256);

    private static long EstimateHighPrecisionMemory(WMImageArtifact artifact, WMOperationExecutionOptions execution) =>
        Math.Min(execution.MemoryBudgetBytes, Math.Max(128L * 1024 * 1024,
            (long)Math.Max(1, artifact.Width) * CalculateTileHeight(artifact.Width, execution.MemoryBudgetBytes) * 4 * 12));

    internal static object DeserializeSettings(
        WMImageOperation operation,
        WMImageArtifact? runtimeArtifact = null)
    {
        if (operation.Kind == WMImageOperationKind.ColorGrade)
            return JsonSerializer.Deserialize<WMColorRecipe>(operation.ParametersJson)
                   ?? throw new InvalidOperationException("无法恢复调色参数。");
        if (operation.Kind == WMImageOperationKind.Crop)
            return JsonSerializer.Deserialize<WMCropSettings>(operation.ParametersJson)
                   ?? throw new InvalidOperationException("无法恢复裁切参数。");
        if (operation.Kind == WMImageOperationKind.Template)
        {
            using var document = JsonDocument.Parse(operation.ParametersJson);
            if (!document.RootElement.TryGetProperty(nameof(WMTemplateOperationSettings.CanvasJson), out var property)
                || string.IsNullOrWhiteSpace(property.GetString()))
                throw new InvalidOperationException("无法恢复模板参数。");
            var canvasJson = property.GetString()!;
            var canvas = Global.ReadConfig(canvasJson);
            if (runtimeArtifact is not null)
            {
                // EXIF is runtime photo data and intentionally excluded from template JSON.
                // Always attach one entry so a metadata-less photo renders empty fields
                // instead of falling back to the template designer's sample metadata.
                canvas.Exif = new Dictionary<string, Dictionary<string, string>>
                {
                    [canvas.ID] = new Dictionary<string, string>(runtimeArtifact.Exif)
                };
            }
            return new WMTemplateOperationSettings(canvas) { CanvasJson = canvasJson };
        }
        throw new InvalidOperationException($"不支持重放操作 {operation.Kind}。");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
