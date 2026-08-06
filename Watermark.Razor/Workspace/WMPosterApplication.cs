#nullable enable

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Builds deterministic output cardinality and slot bindings for both poster
/// entry contexts. It never mutates the stored template.
/// </summary>
public static class WMPosterApplicationPlanner
{
    public static WMPosterApplicationPlan FromTemplateFirst(
        WMCanvas template,
        IReadOnlyList<string> selectedArtifactIds)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(selectedArtifactIds);
        var photoSlots = WMPosterAssetSlots.OrderedPhotoSlots(template);
        var canvasJson = Global.CanvasSerialize(template);
        if (photoSlots.Count == 0)
            return Single(template, canvasJson, []);

        if (photoSlots.Count == 1)
        {
            if (selectedArtifactIds.Count == 0)
                return Single(template, canvasJson, []);
            return new WMPosterApplicationPlan(
                template.ID,
                selectedArtifactIds.Select((artifactId, index) =>
                    Output(
                        template,
                        canvasJson,
                        [new WMPosterAssetBinding(photoSlots[0].Id, artifactId)],
                        index)).ToArray());
        }

        if (selectedArtifactIds.Count > photoSlots.Count)
            throw new InvalidOperationException($"该海报最多选择 {photoSlots.Count} 张照片。");
        var bindings = selectedArtifactIds
            .Select((artifactId, index) =>
                new WMPosterAssetBinding(photoSlots[index].Id, artifactId))
            .ToArray();
        return Single(template, canvasJson, bindings);
    }

    public static WMPosterApplicationPlan FromImagesFirst(
        WMCanvas template,
        IReadOnlyList<string> selectedArtifactIds)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(selectedArtifactIds);
        var canvasJson = Global.CanvasSerialize(template);
        var primary = WMPosterAssetSlots.FindPrimary(template);
        if (primary is null || selectedArtifactIds.Count == 0)
            return Single(template, canvasJson, []);
        return new WMPosterApplicationPlan(
            template.ID,
            selectedArtifactIds.Select((artifactId, index) =>
                Output(
                    template,
                    canvasJson,
                    [new WMPosterAssetBinding(primary.Id, artifactId)],
                    index)).ToArray());
    }

    private static WMPosterApplicationPlan Single(
        WMCanvas template,
        string canvasJson,
        IReadOnlyList<WMPosterAssetBinding> bindings) =>
        new(template.ID, [Output(template, canvasJson, bindings, 0)]);

    private static WMPosterOutputPlan Output(
        WMCanvas template,
        string canvasJson,
        IReadOnlyList<WMPosterAssetBinding> bindings,
        int index) =>
        new(
            Guid.NewGuid().ToString("N"),
            canvasJson,
            bindings,
            $"{SafeName(template.Name)}-{index + 1:00}.png");

    private static string SafeName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "海报" : name.Trim();
        foreach (var character in Path.GetInvalidFileNameChars())
            value = value.Replace(character, '-');
        return value;
    }
}

/// <summary>
/// Resolves one transient output plan into the existing WMCanvas render input.
/// Selected assets are addressed by artifact ID; device paths never enter the
/// persisted template JSON.
/// </summary>
public static class WMPosterRuntimeCanvasResolver
{
    public static WMCanvas Resolve(
        WMPosterOutputPlan output,
        IReadOnlyDictionary<string, WMImageArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(artifacts);
        var canvas = Global.ReadConfig(output.CanvasJson);
        canvas.Exif ??= [];
        foreach (var binding in output.Bindings)
        {
            var slot = canvas.PosterManifest.AssetSlots.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, binding.SlotId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"海报素材槽 {binding.SlotId} 已不存在。");
            if (!artifacts.TryGetValue(binding.ArtifactId, out var artifact))
                throw new InvalidOperationException($"海报素材 {binding.ArtifactId} 已不存在。");
            ApplyOverrides(slot, binding, artifact);

            if (string.Equals(
                    canvas.PosterManifest.PrimaryAssetSlotId,
                    slot.Id,
                    StringComparison.Ordinal))
            {
                canvas.Path = artifact.FilePath;
                canvas.Exif[canvas.ID] = CloneExif(artifact.Exif);
                continue;
            }

            var owner = Global.EnumerateControls(canvas).FirstOrDefault(control =>
                string.Equals(
                    control.PosterMetadata?.AssetSlotId,
                    slot.Id,
                    StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"海报素材槽“{slot.Name}”没有绑定图层。");
            switch (owner)
            {
                case WMLogo logo:
                    logo.Path = artifact.FilePath;
                    logo.Enabled = true;
                    logo.AutoSetLogo = false;
                    break;
                case WMContainer container:
                    container.Path = artifact.FilePath;
                    container.ContainerProperties.Show = true;
                    break;
                default:
                    throw new InvalidOperationException($"图层 {owner.ID} 不支持图片素材。");
            }
            canvas.Exif[owner.ID] = CloneExif(artifact.Exif);
        }
        return canvas;
    }

    private static void ApplyOverrides(
        WMPosterAssetSlot slot,
        WMPosterAssetBinding binding,
        WMImageArtifact artifact)
    {
        if (binding.FitOverride is { } fit)
            slot.Fit = fit;
        if (binding.CropOverride is not { } crop
            || artifact.Width <= 0
            || artifact.Height <= 0)
            return;
        slot.SetCropSettings(crop, artifact.Width, artifact.Height);
    }

    private static Dictionary<string, string> CloneExif(
        IReadOnlyDictionary<string, string>? exif) =>
        exif is null
            ? []
            : new Dictionary<string, string>(exif, StringComparer.Ordinal);
}

/// <summary>
/// Renders transient poster outputs through the existing template renderer.
/// The cache key owns the instance snapshot, ordered slot bindings, crop/fit
/// overrides, source content identities and target canvas dimensions.
/// </summary>
public sealed class WMPosterApplicationProcessor(
    IWMTemplateRenderer renderer,
    IWMArtifactCache cache,
    IWMExecutionProfileProvider executionProfiles,
    IWMWorkspacePerformanceCounters metrics) : IWMPosterApplicationProcessor
{
    private const int PipelineVersion = 1;

    public async Task<IReadOnlyList<WMDerivedMediaOutput>> ExecuteAsync(
        WMPosterApplicationPlan plan,
        IReadOnlyDictionary<string, WMImageArtifact> artifacts,
        string sessionDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        if (plan.Outputs.Count == 0)
            throw new InvalidOperationException("海报应用计划没有输出。");

        var batchId = Guid.NewGuid().ToString("N");
        var results = new List<WMDerivedMediaOutput>(plan.Outputs.Count);
        foreach (var output in plan.Outputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RenderOutputAsync(
                    plan.TemplateId,
                    output,
                    artifacts,
                    sessionDirectory,
                    batchId,
                    cancellationToken)
                .ConfigureAwait(false));
        }
        return results;
    }

    private async Task<WMDerivedMediaOutput> RenderOutputAsync(
        string templateId,
        WMPosterOutputPlan output,
        IReadOnlyDictionary<string, WMImageArtifact> artifacts,
        string sessionDirectory,
        string batchId,
        CancellationToken cancellationToken)
    {
        var canvas = WMPosterRuntimeCanvasResolver.Resolve(output, artifacts);
        if (canvas.HasPrimaryImage && string.IsNullOrWhiteSpace(canvas.Path))
        {
            var defaultPath = Path.Combine(
                Global.AppPath.TemplatesFolder,
                templateId,
                "default.jpg");
            if (File.Exists(defaultPath))
                canvas.Path = defaultPath;
        }
        var fingerprint = CreateFingerprint(templateId, output, artifacts, canvas);
        var cached = await cache.TryGetAsync(sessionDirectory, fingerprint, cancellationToken)
            .ConfigureAwait(false);
        var outputDirectory = Path.Combine(sessionDirectory, "artifacts", "poster");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = cached?.FilePath
                         ?? Path.Combine(
                             outputDirectory,
                             $"poster-{fingerprint[..24].ToLowerInvariant()}.png");

        int width;
        int height;
        if (cached is null)
        {
            byte[] bytes;
            using (metrics.Measure(WMWorkspaceMetricStage.Replay))
            using (metrics.Measure(WMWorkspaceMetricStage.Encode))
                bytes = await renderer.RenderAsync(
                        new WMTemplateRenderRequest(
                            canvas,
                            IsPreview: false,
                            Format: SKEncodedImageFormat.Png,
                            Quality: 100),
                        cancellationToken)
                    .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = new SKMemoryStream(bytes))
            using (var codec = SKCodec.Create(stream)
                               ?? throw new InvalidDataException("海报渲染产物无法读取。"))
            {
                width = codec.Info.Width;
                height = codec.Info.Height;
            }

            var temporary = outputPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken)
                    .ConfigureAwait(false);
                File.Move(temporary, outputPath, true);
                await cache.CommitAsync(
                        sessionDirectory,
                        fingerprint,
                        outputPath,
                        executionProfiles.GetInteractiveProfile().PreviewCacheBudgetBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                TryDelete(temporary);
            }
        }
        else
        {
            using var codec = SKCodec.Create(outputPath)
                              ?? throw new InvalidDataException("缓存的海报渲染产物无法读取。");
            width = codec.Info.Width;
            height = codec.Info.Height;
        }

        var inputIds = output.Bindings
            .Select(binding => binding.ArtifactId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var artifactId = Guid.NewGuid().ToString("N");
        var operation = WMImageOperation.Create(
            WMImageOperationKind.Template,
            inputIds,
            [artifactId],
            output,
            batchId);
        var artifact = new WMImageArtifact
        {
            Id = artifactId,
            FilePath = outputPath,
            PreviewPath = outputPath,
            ParentArtifactIds = inputIds,
            SourceOperation = WMImageOperationKind.Template,
            OperationId = operation.Id,
            ContentHash = fingerprint,
            Width = width,
            Height = height,
            ColorSpace = "sRGB",
            CanvasSnapshotJson = output.CanvasJson
        };
        return new WMDerivedMediaOutput(artifact, operation, output.SuggestedFileName);
    }

    internal static string CreateFingerprint(
        string templateId,
        WMPosterOutputPlan output,
        IReadOnlyDictionary<string, WMImageArtifact> artifacts,
        WMCanvas canvas)
    {
        var builder = new StringBuilder()
            .Append("wm-poster-v").Append(PipelineVersion)
            .Append('|').Append(templateId)
            .Append('|').Append(output.CanvasJson)
            .Append("|target:")
            .Append(canvas.CanvasSizing.Mode).Append(':')
            .Append(canvas.CanvasSizing.ReferenceWidth).Append('x')
            .Append(canvas.CanvasSizing.ReferenceHeight);
        foreach (var binding in output.Bindings)
        {
            if (!artifacts.TryGetValue(binding.ArtifactId, out var artifact))
                throw new InvalidOperationException($"海报素材 {binding.ArtifactId} 已不存在。");
            var info = new FileInfo(artifact.FilePath);
            builder.Append("|slot:").Append(binding.SlotId)
                .Append("|fit:").Append(binding.FitOverride?.ToString() ?? "default")
                .Append("|crop:").Append(JsonSerializer.Serialize(binding.CropOverride))
                .Append("|asset:")
                .Append(artifact.ContentHash ?? artifact.SourceFingerprint?.StableId ?? artifact.Id)
                .Append(':').Append(info.Exists ? info.Length : 0)
                .Append(':').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0);
        }
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
