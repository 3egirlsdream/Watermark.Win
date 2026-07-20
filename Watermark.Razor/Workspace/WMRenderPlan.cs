#nullable enable

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed record WMRenderPlanStep(WMImageOperation Operation);

public sealed record WMRenderPlan(
    WMImageArtifact BaseArtifact,
    IReadOnlyList<WMRenderPlanStep> Steps,
    WMImageArtifact CurrentArtifact)
{
    public bool RequiresReplay => Steps.Count > 0;

    public bool HasCommittedHighPrecision =>
        !string.Equals(CurrentArtifact.Id, BaseArtifact.Id, StringComparison.Ordinal)
        && CurrentArtifact.HighPrecision is { FilePath.Length: > 0 } highPrecision
        && File.Exists(highPrecision.FilePath);
}

public enum WMRenderPurpose
{
    InteractiveBase,
    SettledPreview,
    Export
}

public sealed record WMRenderTarget(
    WMRenderPurpose Purpose,
    int? MaximumLongEdge,
    WMExportFormat Format,
    int Quality,
    bool IncludeMetadata)
{
    public static WMRenderTarget InteractiveBase(int maximumLongEdge = 1600) =>
        new(WMRenderPurpose.InteractiveBase, maximumLongEdge, WMExportFormat.Jpeg8, 100, false);

    public static WMRenderTarget SettledPreview(int maximumLongEdge = 1600) =>
        new(WMRenderPurpose.SettledPreview, maximumLongEdge, WMExportFormat.Jpeg8, 100, false);
}

public sealed record WMCompiledRenderPlan(
    WMImageArtifact BaseArtifact,
    IReadOnlyList<WMRenderPlanStep> Steps,
    string GraphFingerprint,
    int PipelineVersion,
    WMRenderTarget Target)
{
    public WMRenderPlan ToRenderPlan() => new(BaseArtifact, Steps, BaseArtifact);
}

public sealed record WMRenderedArtifact(
    string FilePath,
    string Fingerprint,
    string MimeType,
    int Width,
    int Height,
    bool CacheHit = false);

public interface IWMRenderPlanCompiler
{
    Task<WMCompiledRenderPlan> CompileAsync(
        WMWorkspaceSession session,
        string mediaId,
        WMRenderTarget target,
        CancellationToken token);
}

public interface IWMRenderExecutor
{
    Task<WMRenderedArtifact> ExecuteAsync(
        WMCompiledRenderPlan plan,
        CancellationToken token);
}

public sealed class WMRenderExecutor(
    WMWorkspacePreviewService previewService,
    WMFullResolutionRenderService fullResolutionRenderService) : IWMRenderExecutor
{
    public async Task<WMRenderedArtifact> ExecuteAsync(
        WMCompiledRenderPlan plan,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Target.Purpose == WMRenderPurpose.Export)
        {
            var path = await fullResolutionRenderService.ExportAsync(plan, token).ConfigureAwait(false);
            return new WMRenderedArtifact(
                path,
                plan.GraphFingerprint,
                MimeFromFormat(plan.Target.Format),
                plan.BaseArtifact.Width,
                plan.BaseArtifact.Height,
                CacheHit: false);
        }
        var preview = await previewService.RenderAsync(plan, 0, token).ConfigureAwait(false);
        return new WMRenderedArtifact(
            preview.FilePath,
            preview.Fingerprint,
            preview.MimeType,
            preview.Width,
            preview.Height,
            preview.CacheHit);
    }

    private static string MimeFromFormat(WMExportFormat format) => format switch
    {
        WMExportFormat.Png16 => "image/png",
        WMExportFormat.Tiff16 => "image/tiff",
        _ => "image/jpeg"
    };
}

/// <summary>
/// Produces the only operation projection consumed by previews and exports.
/// InteractiveBase deliberately omits the terminal colour step so the same
/// upstream artifact can be uploaded to WebGL once and reused while dragging.
/// </summary>
public sealed class WMRenderPlanCompiler : IWMRenderPlanCompiler
{
    public Task<WMCompiledRenderPlan> CompileAsync(
        WMWorkspaceSession session,
        string mediaId,
        WMRenderTarget target,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        ArgumentNullException.ThrowIfNull(target);
        token.ThrowIfCancellationRequested();

        var media = session.Media.FirstOrDefault(item => item.Id == mediaId)
                    ?? session.MediaCatalog.FirstOrDefault(item => item.Id == mediaId)
                    ?? throw new InvalidOperationException("工作台素材不存在。");
        var artifact = ResolveArtifact(session, media);
        var steps = new List<WMRenderPlanStep>(3);
        var effective = ResolveEffectiveEdits(session, media, artifact);
        if (!WMCropPlanner.IsIdentity(effective.CropSettings))
        {
            steps.Add(new WMRenderPlanStep(CreateOperation(
                WMImageOperationKind.Crop,
                artifact.Id,
                effective.CropSettings)));
        }
        var templateId = effective.TemplateId;
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            if (string.IsNullOrWhiteSpace(effective.TemplateSnapshotJson))
                throw new InvalidOperationException("会话模板快照已丢失，不能从实时模板文件替代恢复结果。");
            var canvas = Global.ReadConfig(effective.TemplateSnapshotJson);
            canvas = Global.ReadConfig(Global.CanvasSerialize(canvas));
            canvas.Path = string.Empty;
            steps.Add(new WMRenderPlanStep(CreateOperation(
                WMImageOperationKind.Template,
                steps.Count == 0 ? artifact.Id : steps[^1].Operation.OutputArtifactIds[0],
                new WMTemplateOperationSettings(canvas))));
        }

        if (target.Purpose != WMRenderPurpose.InteractiveBase)
        {
            var recipe = effective.ColorRecipe;
            if (recipe is not null)
            {
                recipe = CloneRecipe(recipe);
                recipe.UpgradeToCurrentSchema();
                steps.Add(new WMRenderPlanStep(CreateOperation(
                    WMImageOperationKind.ColorGrade,
                    steps.Count == 0 ? artifact.Id : steps[^1].Operation.OutputArtifactIds[0],
                    recipe)));
            }
        }

        var fingerprint = CreateGraphFingerprint(artifact, steps);
        return Task.FromResult(new WMCompiledRenderPlan(
            artifact,
            steps,
            fingerprint,
            WMColorPipelineVersion.Current,
            target with
            {
                MaximumLongEdge = target.MaximumLongEdge is null
                    ? null
                    : Math.Clamp(target.MaximumLongEdge.Value, 320, 16384),
                Quality = Math.Clamp(target.Quality, 60, 100)
            }));
    }

    private static WMImageOperation CreateOperation<TSettings>(
        WMImageOperationKind kind,
        string inputArtifactId,
        TSettings settings)
    {
        var parameters = JsonSerializer.Serialize(settings);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}|{parameters}")))[..24];
        return new WMImageOperation
        {
            Id = $"plan-{id}",
            BatchId = "compiled-render-plan",
            Kind = kind,
            InputArtifactIds = [inputArtifactId],
            OutputArtifactIds = [$"plan-output-{id}"],
            ParametersJson = parameters,
            CreatedAtUtc = DateTime.UnixEpoch
        };
    }

    private static string CreateGraphFingerprint(
        WMImageArtifact artifact,
        IReadOnlyList<WMRenderPlanStep> steps)
    {
        var builder = new StringBuilder("wm-render-graph-v1|")
            .Append(artifact.SourceFingerprint?.StableId ?? artifact.ContentHash ?? artifact.Id)
            .Append('|').Append(WMColorPipelineVersion.Current);
        foreach (var step in steps)
            builder.Append('|').Append((int)step.Operation.Kind).Append(':').Append(step.Operation.ParametersJson);
        if (steps.Any(step => step.Operation.Kind == WMImageOperationKind.Template))
        {
            builder.Append("|exif");
            foreach (var pair in artifact.Exif.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                builder.Append('|').Append(pair.Key.Length).Append(':').Append(pair.Key)
                    .Append('=').Append(pair.Value?.Length ?? 0).Append(':').Append(pair.Value);
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static WMImageArtifact ResolveArtifact(WMWorkspaceSession session, WMWorkspaceMedia media)
    {
        if (session.CurrentArtifactIdsByMediaId.TryGetValue(media.Id, out var artifactId))
        {
            var current = session.Artifacts.FirstOrDefault(item => item.Id == artifactId);
            if (current is not null) return current;
        }
        return media.Artifact;
    }

    private static EffectiveEdits ResolveEffectiveEdits(
        WMWorkspaceSession session,
        WMWorkspaceMedia media,
        WMImageArtifact artifact)
    {
        var related = new HashSet<string>(StringComparer.Ordinal) { media.Artifact.Id, artifact.Id };
        var transactionOperationIds = session.Transactions
            .SelectMany(transaction => transaction.EffectiveOperations)
            .Select(operation => operation.Id)
            .ToHashSet(StringComparer.Ordinal);
        var appliedTransactions = session.Transactions
            .Take(Math.Clamp(session.HistoryCursor, 0, session.Transactions.Count))
            .ToArray();
        var targetsByOperation = appliedTransactions
            .SelectMany(transaction => transaction.Assignments)
            .SelectMany(assignment => assignment.Operations.Select(operation =>
                new KeyValuePair<string, IReadOnlyList<string>>(operation.Id, assignment.MediaIds)))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(pair => pair.Value).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        var operations = session.Operations
            .Where(operation => !transactionOperationIds.Contains(operation.Id))
            .Concat(appliedTransactions.SelectMany(transaction => transaction.EffectiveOperations));
        string? templateId = session.TemplateId;
        string? templateSnapshot = artifact.CanvasSnapshotJson;
        var colorRecipe = CloneRecipeOrNull(session.ColorRecipe);
        var cropSettings = session.CropSettingsByMediaId.TryGetValue(media.Id, out var projectedCrop)
            ? projectedCrop
            : WMCropSettings.Identity;
        foreach (var operation in operations)
        {
            var targetsMedia = targetsByOperation.TryGetValue(operation.Id, out var assigned)
                ? assigned.Contains(media.Id)
                : operation.InputArtifactIds.Any(related.Contains);
            if (!targetsMedia) continue;
            try
            {
                if (operation.Kind == WMImageOperationKind.Template)
                {
                    var selection = JsonSerializer.Deserialize<WMWorkspaceTemplateSelection>(
                        operation.ParametersJson);
                    if (selection is not null)
                    {
                        templateId = selection.TemplateId;
                        templateSnapshot = selection.CanvasJson;
                    }
                }
                else if (operation.Kind == WMImageOperationKind.ColorGrade)
                {
                    colorRecipe = JsonSerializer.Deserialize<WMColorRecipe>(operation.ParametersJson);
                }
                else if (operation.Kind == WMImageOperationKind.Crop)
                {
                    cropSettings = JsonSerializer.Deserialize<WMCropSettings>(operation.ParametersJson)
                                   ?? WMCropSettings.Identity;
                }
            }
            catch (JsonException)
            {
                // Recovery owns corrupt operation reporting; compilation keeps the last valid edit.
            }
        }
        if (session.TemplateIdsByMediaId.TryGetValue(media.Id, out var projectedTemplate))
            templateId = projectedTemplate;
        if (session.ColorRecipesByMediaId.TryGetValue(media.Id, out var projectedRecipe))
            colorRecipe = CloneRecipeOrNull(projectedRecipe);
        if (session.CropSettingsByMediaId.TryGetValue(media.Id, out projectedCrop))
            cropSettings = projectedCrop;
        return new EffectiveEdits(templateId, templateSnapshot, colorRecipe, cropSettings);
    }

    private static WMColorRecipe CloneRecipe(WMColorRecipe recipe) =>
        WMColorRecipeSnapshot.Copy(recipe)!;

    private static WMColorRecipe? CloneRecipeOrNull(WMColorRecipe? recipe) =>
        recipe is null ? null : CloneRecipe(recipe);

    private sealed record EffectiveEdits(
        string? TemplateId,
        string? TemplateSnapshotJson,
        WMColorRecipe? ColorRecipe,
        WMCropSettings CropSettings);
}
