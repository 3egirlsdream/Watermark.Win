#nullable enable

using System.Security.Cryptography;
using System.Text;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Cross-platform façade over the existing fingerprinted JPEG replay/export
/// pipeline. High precision formats remain capability-gated for phase two.
/// </summary>
public sealed class WMFullResolutionRenderService(
    WMFastJpegExportService fastJpegExportService,
    IWMExecutionProfileProvider executionProfiles,
    WMFullResolutionRenderPipeline? fullResolutionPipeline = null,
    IWMRenderPlanCompiler? renderPlanCompiler = null)
{
    private readonly IWMRenderPlanCompiler planCompiler = renderPlanCompiler ?? new WMRenderPlanCompiler();

    public Task<string> ExportAsync(
        WMCompiledRenderPlan compiled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        if (compiled.Target.Purpose != WMRenderPurpose.Export)
            throw new ArgumentException("编译计划不是导出目标。", nameof(compiled));
        var media = new WMWorkspaceMedia
        {
            Id = compiled.BaseArtifact.Id,
            DisplayName = Path.GetFileName(compiled.BaseArtifact.FilePath),
            OriginalReference = compiled.BaseArtifact.FilePath,
            Artifact = compiled.BaseArtifact
        };
        return ExportPlanAsync(
            compiled.ToRenderPlan(),
            media,
            compiled.Target.Format,
            compiled.Target.Quality,
            compiled.Target.MaximumLongEdge,
            cancellationToken);
    }

    public async Task<string> ExportAsync(
        WMWorkspaceSession session,
        WMWorkspaceMedia media,
        WMExportFormat format,
        int quality = 92,
        int? maximumLongEdge = null,
        CancellationToken cancellationToken = default)
    {
        var compiled = await planCompiler.CompileAsync(
            session,
            media.Id,
            new WMRenderTarget(
                WMRenderPurpose.Export,
                maximumLongEdge,
                format,
                quality,
                IncludeMetadata: true),
            cancellationToken).ConfigureAwait(false);
        return await ExportPlanAsync(
            compiled.ToRenderPlan(),
            media with { Artifact = compiled.BaseArtifact },
            format,
            quality,
            maximumLongEdge,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ExportPlanAsync(
        WMRenderPlan plan,
        WMWorkspaceMedia media,
        WMExportFormat format,
        int quality,
        int? maximumLongEdge,
        CancellationToken cancellationToken)
    {
        var sessionDirectory = Path.GetDirectoryName(
            Path.GetDirectoryName(plan.BaseArtifact.PreviewPath ?? plan.BaseArtifact.FilePath)!)!;
        var exportDirectory = Path.Combine(sessionDirectory, "exports");
        Directory.CreateDirectory(exportDirectory);
        var normalizedQuality = Math.Clamp(quality, 60, 100);
        var resolution = maximumLongEdge is null
            ? "default"
            : $"max:{Math.Clamp(maximumLongEdge.Value, 320, 16384)}";
        var outputFingerprint = CreateOutputFingerprint(plan, resolution, normalizedQuality, format);
        var extension = format switch
        {
            WMExportFormat.Png16 => ".png",
            WMExportFormat.Tiff16 => ".tiff",
            _ => ".jpg"
        };
        var output = Path.Combine(exportDirectory, $"{media.Id}-{outputFingerprint[..24]}{extension}");
        if (File.Exists(output) && new FileInfo(output).Length > 0) return output;
        TryDelete(output);
        var request = new WMFullResolutionRenderRequest(
            plan,
            output,
            plan.BaseArtifact.FilePath,
            resolution,
            normalizedQuality,
            sessionDirectory,
            executionProfiles.GetInteractiveProfile(),
            format);
        if (format != WMExportFormat.Jpeg8
            || media.Artifact.HighPrecision is { FilePath.Length: > 0 } highPrecision
            && File.Exists(highPrecision.FilePath))
        {
            if (fullResolutionPipeline is null)
                throw new InvalidOperationException("当前宿主未注册高精度导出管线。");
            await fullResolutionPipeline.RenderAsync(
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await fastJpegExportService.RenderAsync(
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        return output;
    }

    private static string CreateOutputFingerprint(
        WMRenderPlan plan,
        string resolution,
        int quality,
        WMExportFormat format)
    {
        var builder = new StringBuilder("wm-workspace-export-v2|")
            .Append(plan.BaseArtifact.SourceFingerprint?.StableId
                    ?? plan.BaseArtifact.ContentHash
                    ?? plan.BaseArtifact.Id)
            .Append('|').Append(resolution)
            .Append('|').Append(quality)
            .Append('|').Append(format);
        foreach (var step in plan.Steps)
            builder.Append('|')
                .Append((int)step.Operation.Kind)
                .Append(':')
                .Append(step.Operation.ParametersJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
