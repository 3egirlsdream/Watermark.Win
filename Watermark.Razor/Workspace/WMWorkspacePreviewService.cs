#nullable enable

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Executes the interactive operation graph in memory. A template followed by
/// color grading shares one decoded proxy and produces one encoded preview.
/// </summary>
public sealed class WMWorkspacePreviewService(
    IWMTemplateRenderer templateRenderer,
    WMColorGradeOperationProcessor colorProcessor,
    IWMProcessingScheduler scheduler,
    IWMExecutionProfileProvider executionProfiles,
    IWMWorkspacePerformanceCounters metrics,
    IWMArtifactCache? persistentCache = null)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> cacheLocks = new(StringComparer.Ordinal);
    private readonly IWMArtifactCache artifactCache = persistentCache ?? new WMArtifactCache();

    public Task<string> CreateFingerprintAsync(
        WMCompiledRenderPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        var material = $"workspace-preview-v3|{plan.GraphFingerprint}|{plan.Target.MaximumLongEdge}|"
                       + $"{plan.PipelineVersion}|png|100";
        return Task.FromResult(Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material))));
    }

    public async Task<WMWorkspacePreview> RenderAsync(
        WMCompiledRenderPlan plan,
        long version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var fingerprint = await CreateFingerprintAsync(plan, cancellationToken).ConfigureAwait(false);
        if (plan.Steps.Count == 0)
        {
            var preview = plan.BaseArtifact.PreviewPath ?? plan.BaseArtifact.FilePath;
            return new WMWorkspacePreview(
                version, fingerprint, preview, MimeFromPath(preview),
                plan.BaseArtifact.Width, plan.BaseArtifact.Height, CacheHit: true);
        }

        var sessionDirectory = ResolveSessionDirectory(plan.BaseArtifact);
        var execution = executionProfiles.GetInteractiveProfile().Normalize();
        var maximumEdge = plan.Target.MaximumLongEdge ?? execution.PreviewMaxEdge;
        var outputDirectory = Path.Combine(sessionDirectory, "operations", "compiled-preview");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"{fingerprint}.png");
        var cacheLock = cacheLocks.GetOrAdd(outputPath, _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cached = await artifactCache.TryGetAsync(
                sessionDirectory, fingerprint, cancellationToken).ConfigureAwait(false);
            if (cached is not null && TryReadCachedSize(cached.FilePath, out var width, out var height))
                return new WMWorkspacePreview(
                    version, fingerprint, cached.FilePath, "image/png", width, height, CacheHit: true);

            var sourcePath = plan.BaseArtifact.PreviewPath is { Length: > 0 } proxy && File.Exists(proxy)
                ? proxy
                : plan.BaseArtifact.FilePath;
            var estimatedMemory = Math.Max(
                64L * 1024 * 1024,
                (long)Math.Max(1, maximumEdge) * Math.Max(1, maximumEdge) * 32L);
            var rendered = await scheduler.RunAsync(
                parallelOptions => RenderCompiledGraph(
                    sourcePath, plan, maximumEdge, parallelOptions, cancellationToken),
                execution,
                estimatedMemory,
                cancellationToken).ConfigureAwait(false);
            var temporary = outputPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(
                                 temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await stream.WriteAsync(rendered.Bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temporary, outputPath, true);
            }
            finally
            {
                TryDelete(temporary);
            }
            await artifactCache.CommitAsync(
                sessionDirectory,
                fingerprint,
                outputPath,
                execution.PreviewCacheBudgetBytes,
                cancellationToken).ConfigureAwait(false);
            return new WMWorkspacePreview(
                version, fingerprint, outputPath, "image/png", rendered.Width, rendered.Height);
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private RenderedPreview RenderCompiledGraph(
        string sourcePath,
        WMCompiledRenderPlan plan,
        int maximumEdge,
        ParallelOptions parallelOptions,
        CancellationToken cancellationToken)
    {
        SKBitmap current;
        using (metrics.Measure(WMWorkspaceMetricStage.Decode))
        using (var codec = SKCodec.Create(sourcePath)
                           ?? throw new InvalidOperationException($"无法读取图片：{Path.GetFileName(sourcePath)}"))
        using (var decoded = SKBitmap.Decode(codec)
                             ?? throw new InvalidOperationException($"无法解码图片：{Path.GetFileName(sourcePath)}"))
        {
            var oriented = WatermarkHelper.AutoOrient(codec, decoded);
            using var orientedOwner = ReferenceEquals(oriented, decoded) ? null : oriented;
            current = WMImageBitmap.NormalizeToSrgb(oriented);
        }
        var prepared = ResizeIfNeeded(current, maximumEdge);
        if (!ReferenceEquals(prepared, current))
        {
            current.Dispose();
            current = prepared;
        }
        try
        {
            using (metrics.Measure(WMWorkspaceMetricStage.Replay))
            {
                foreach (var step in plan.Steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    parallelOptions.CancellationToken.ThrowIfCancellationRequested();
                    SKBitmap next;
                    if (step.Operation.Kind == WMImageOperationKind.Template)
                    {
                        var settings = (WMTemplateOperationSettings)
                            WMFullResolutionRenderPipeline.DeserializeSettings(step.Operation);
                        next = templateRenderer.RenderBitmap(settings.Canvas, current, cancellationToken);
                    }
                    else if (step.Operation.Kind == WMImageOperationKind.ColorGrade)
                    {
                        var recipe = JsonSerializer.Deserialize<WMColorRecipe>(step.Operation.ParametersJson)
                                     ?? throw new InvalidOperationException("无法恢复调色参数。");
                        next = colorProcessor.ApplyToBitmap(
                            current, plan.BaseArtifact, recipe, parallelOptions);
                    }
                    else
                    {
                        throw new InvalidOperationException($"稳定预览不支持操作 {step.Operation.Kind}。");
                    }
                    current.Dispose();
                    current = next;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using (metrics.Measure(WMWorkspaceMetricStage.Encode))
            using (var image = SKImage.FromBitmap(current))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100)
                              ?? throw new InvalidOperationException("无法编码工作台预览。"))
            {
                return new RenderedPreview(data.ToArray(), current.Width, current.Height);
            }
        }
        finally
        {
            current.Dispose();
        }
    }

    private SKBitmap ResizeIfNeeded(SKBitmap source, int maximumEdge)
    {
        var edge = Math.Max(source.Width, source.Height);
        if (edge <= maximumEdge) return source;
        var scale = maximumEdge / (float)edge;
        using (metrics.Measure(WMWorkspaceMetricStage.Scale))
        {
            return source.Resize(
                       new SKImageInfo(
                           Math.Max(1, (int)Math.Round(source.Width * scale)),
                           Math.Max(1, (int)Math.Round(source.Height * scale)),
                           SKColorType.Bgra8888,
                           SKAlphaType.Premul,
                           SKColorSpace.CreateSrgb()),
                       SKFilterQuality.Medium)
                   ?? throw new InvalidOperationException("无法缩放工作台预览。");
        }
    }

    private static string ResolveSessionDirectory(WMImageArtifact artifact)
    {
        var path = artifact.PreviewPath ?? artifact.FilePath;
        var containingDirectory = Directory.GetParent(path)
                                  ?? throw new InvalidOperationException("素材路径没有有效目录。");
        return containingDirectory.Parent?.FullName ?? containingDirectory.FullName;
    }

    private static bool TryReadCachedSize(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
            using var codec = SKCodec.Create(path);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return false;
            width = codec.Info.Width;
            height = codec.Info.Height;
            return true;
        }
        catch
        {
            TryDelete(path);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string MimeFromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    private sealed record RenderedPreview(byte[] Bytes, int Width, int Height);
}
