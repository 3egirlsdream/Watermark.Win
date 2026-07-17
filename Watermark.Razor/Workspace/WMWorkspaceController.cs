#nullable enable

using Watermark.Shared.Models;
using System.Runtime.CompilerServices;

namespace Watermark.Razor.Workspace;

/// <summary>
/// The single workspace state owner. Durable commands are serialized while
/// previews remain latest-wins. Epoch, intent revision and Blob generation are
/// checked independently so work from a closed or superseded session cannot
/// save or publish UI state.
/// </summary>
public sealed class WMWorkspaceController
{
    private const string PreviewOwner = "workspace:preview";
    private const string OriginalOwner = "workspace:original";
    private const string ColorBaseOwner = "workspace:color-base";
    private const string MediaPreviewOwnerPrefix = "workspace:media-preview:";
    private readonly object gate = new();
    private readonly IWMWorkspaceSessionStore sessionStore;
    private readonly IWMWorkspaceRenderCoordinator renderCoordinator;
    private readonly IWMObjectUrlRegistry objectUrls;
    private readonly WMWorkspacePreviewService previewService;
    private readonly WMFullResolutionRenderService exportService;
    private readonly IWMExportSink exportSink;
    private readonly WMTemplateSnapshotService templateSnapshots;
    private readonly IWMArtifactCache artifactCache;
    private readonly IWMImagingCapabilityProvider? imagingCapabilities;
    private readonly IWMImageStackEngine? imageStackEngine;
    private readonly IWMExecutionProfileProvider? executionProfiles;
    private readonly WMImageImportService? imageImporter;
    private readonly IWMDerivedMediaProcessor? derivedMediaProcessor;
    private readonly IWMColorReferenceService? colorReferenceService;
    private readonly IWMColorPresetLibrary? colorPresetLibrary;
    private readonly IWMWorkspaceTraceStore? traceStore;
    private readonly IWMWorkspacePerformanceCounters? performanceCounters;
    private readonly IWMRenderPlanCompiler renderPlanCompiler;
    private readonly IWMColorPipelineCompiler? colorPipelineCompiler;
    private readonly WMDurableCommandQueue durableCommands = new();
    private readonly WMTransientPreviewCoordinator transientPreviews = new();
    private readonly WMWorkspaceJobCoordinator jobs = new();
    private readonly WMWorkspaceRecoveryService recoveryService;
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly HashSet<Task> ownedTasks = [];
    private readonly Dictionary<string, WMObjectUrlLease> mediaPreviewLeases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> mediaPreviewFingerprints = new(StringComparer.Ordinal);
    private WMWorkspaceSession? session;
    private WMWorkspaceState state = new();
    private WMColorRecipe? draftColorRecipe;
    private WMWorkspaceTemplateEdit? draftTemplateEdit;
    private WMObjectUrlLease? previewLease;
    private WMObjectUrlLease? originalLease;
    private WMObjectUrlLease? colorReferenceLease;
    private WMObjectUrlLease? colorBaseLease;
    private IDisposable? previewArtifactLease;
    private IDisposable? colorBaseArtifactLease;
    private CancellationTokenSource? epochCancellation;
    private IDisposable? sessionLease;
    private long epoch;
    private long intentRevision;
    private long nextPreviewVersion;
    private long nextBlobVersion;
    private bool closed = true;
    private string? colorReferencePath;
    private string? colorReferenceName;
    private string? colorBaseFingerprint;
    private Task<ColorBasePreview>? colorBaseTask;
    private bool? gpuColorPreviewAvailable;

    public WMWorkspaceController(
        IWMWorkspaceSessionStore sessionStore,
        IWMWorkspaceRenderCoordinator renderCoordinator,
        IWMObjectUrlRegistry objectUrls,
        WMWorkspacePreviewService previewService,
        WMFullResolutionRenderService exportService,
        IWMExportSink exportSink,
        WMTemplateSnapshotService? templateSnapshots = null,
        IWMArtifactCache? artifactCache = null,
        IWMImagingCapabilityProvider? imagingCapabilities = null,
        IWMImageStackEngine? imageStackEngine = null,
        IWMExecutionProfileProvider? executionProfiles = null,
        WMImageImportService? imageImporter = null,
        IWMDerivedMediaProcessor? derivedMediaProcessor = null,
        IWMColorReferenceService? colorReferenceService = null,
        IWMColorPresetLibrary? colorPresetLibrary = null,
        IWMWorkspaceTraceStore? traceStore = null,
        IWMWorkspacePerformanceCounters? performanceCounters = null,
        IWMRenderPlanCompiler? renderPlanCompiler = null,
        IWMColorPipelineCompiler? colorPipelineCompiler = null)
    {
        this.sessionStore = sessionStore;
        this.renderCoordinator = renderCoordinator;
        this.objectUrls = objectUrls;
        this.previewService = previewService;
        this.exportService = exportService;
        this.exportSink = exportSink;
        this.templateSnapshots = templateSnapshots ?? new WMTemplateSnapshotService();
        this.artifactCache = artifactCache ?? new WMArtifactCache();
        this.imagingCapabilities = imagingCapabilities;
        this.imageStackEngine = imageStackEngine;
        this.executionProfiles = executionProfiles;
        this.imageImporter = imageImporter;
        this.derivedMediaProcessor = derivedMediaProcessor;
        this.colorReferenceService = colorReferenceService;
        this.colorPresetLibrary = colorPresetLibrary;
        this.traceStore = traceStore;
        this.performanceCounters = performanceCounters;
        this.renderPlanCompiler = renderPlanCompiler ?? new WMRenderPlanCompiler();
        this.colorPipelineCompiler = colorPipelineCompiler;
        recoveryService = new WMWorkspaceRecoveryService(sessionStore);
    }

    public event Action<WMWorkspaceState> Changed = delegate { };

    public WMWorkspaceState State
    {
        get { lock (gate) return state; }
    }

    public string? GetMediaPreviewUrl(string mediaId)
    {
        if (string.IsNullOrWhiteSpace(mediaId)) return null;
        lock (gate)
            return mediaPreviewLeases.TryGetValue(mediaId, out var lease) ? lease.Url : null;
    }

    public WMWorkspaceTemplateDraft? TemplateDraft
    {
        get { lock (gate) return session?.TemplateDraft; }
    }

    public Task SaveTemplateDraftAsync(
        WMWorkspaceTemplateDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return RunDurableCommandAsync(async context =>
        {
            if (context.Session.Mode != WMWorkspaceMode.TemplateDesign)
                throw new InvalidOperationException("当前会话不是模板设计模式。");
            var updated = NextRevision(context.Session with
            {
                TemplateDraft = draft with { UpdatedAtUtc = DateTime.UtcNow }
            });
            CommitPassiveSession(context.Epoch, updated, value => value);
            await PersistAsync(updated, context.Epoch, context.Token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task ClearTemplateDraftAsync(CancellationToken cancellationToken = default) =>
        RunDurableCommandAsync(async context =>
        {
            if (context.Session.TemplateDraft is null) return;
            var updated = NextRevision(context.Session with { TemplateDraft = null });
            CommitPassiveSession(context.Epoch, updated, value => value);
            await PersistAsync(updated, context.Epoch, context.Token).ConfigureAwait(false);
        }, cancellationToken);

    public async Task<bool> OpenAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await RecordLogSafeAsync(
            WMDiagnosticLogLevel.Information,
            "workspace-open-started",
            "开始打开工作台会话。",
            sessionId).ConfigureAwait(false);
        long openingEpoch;
        CancellationToken epochToken;
        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CloseEpochAsync().ConfigureAwait(false);
            lock (gate)
            {
                closed = false;
                openingEpoch = ++epoch;
                intentRevision = 0;
                epochCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                epochToken = epochCancellation.Token;
                state = new WMWorkspaceState
                {
                    SessionId = sessionId,
                    Activity = WMWorkspaceActivity.Loading,
                    Message = "正在恢复编辑会话…",
                    CanCancel = true
                };
            }
            sessionLease = sessionStore.AcquireLease(sessionId);
            Changed.Invoke(State);
        }
        finally
        {
            lifecycleLock.Release();
        }

        WMWorkspaceOpenResult openResult;
        try
        {
            openResult = await recoveryService.OpenAsync(sessionId, epochToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!IsCurrent(openingEpoch))
        {
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Information,
                "workspace-open-superseded",
                "会话打开请求已被新会话替代。",
                sessionId).ConfigureAwait(false);
            return false;
        }
        catch (Exception ex)
        {
            TryUpdate(openingEpoch, null, current => current with
            {
                Activity = WMWorkspaceActivity.Failed,
                ErrorMessage = ex.Message,
                Message = "无法打开会话",
                CanCancel = false
            });
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Error,
                "workspace-open-failed",
                ex.Message,
                sessionId,
                ex).ConfigureAwait(false);
            return false;
        }
        if (!IsCurrent(openingEpoch)) return false;
        if (!openResult.IsOpened)
        {
            TryUpdate(openingEpoch, null, current => current with
            {
                Activity = WMWorkspaceActivity.Failed,
                ErrorMessage = WMWorkspaceProjection.RecoveryMessage(openResult),
                Message = "无法恢复会话",
                CanCancel = false,
                Recovery = openResult
            });
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Warning,
                "workspace-open-recovery-required",
                WMWorkspaceProjection.RecoveryMessage(openResult),
                sessionId,
                properties: new Dictionary<string, string>
                {
                    ["status"] = openResult.Status.ToString(),
                    ["issueCount"] = openResult.Issues.Count.ToString()
                }).ConfigureAwait(false);
            return false;
        }
        var opened = openResult.Session!;

        // Sessions created from a template card carry only TemplateId until the
        // controller opens them. Convert that intent into the same immutable
        // operation/transaction used by in-workspace template selection before
        // the first preview is allowed to render.
        if (opened.Mode != WMWorkspaceMode.TemplateDesign
            && !string.IsNullOrWhiteSpace(opened.TemplateId)
            && !opened.Operations.Any(item => item.Kind == WMImageOperationKind.Template))
        {
            try
            {
                var initialTemplateId = opened.TemplateId;
                var canvas = await Global.GetCanvas(initialTemplateId).WaitAsync(epochToken).ConfigureAwait(false)
                             ?? throw new InvalidOperationException("会话使用的模板已不存在。");
                var canvasJson = await templateSnapshots.CreateAsync(
                    opened, initialTemplateId, canvas, epochToken).ConfigureAwait(false);
                var mediaIds = opened.Media.Select(item => item.Id).ToArray();
                opened = AppendTransaction(
                    opened with { TemplateId = null },
                    "应用模板",
                    WMImageOperationKind.Template,
                    mediaIds,
                    new WMWorkspaceTemplateSelection(initialTemplateId, canvasJson));
                opened = NextRevision(opened);
                await sessionStore.SaveAsync(opened, epochToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!IsCurrent(openingEpoch))
            {
                return false;
            }
            catch (Exception ex)
            {
                TryUpdate(openingEpoch, null, current => current with
                {
                    Activity = WMWorkspaceActivity.Failed,
                    ErrorMessage = ex.Message,
                    Message = "无法恢复模板",
                    CanCancel = false
                });
                await RecordLogSafeAsync(
                    WMDiagnosticLogLevel.Error,
                    "initial-template-restore-failed",
                    ex.Message,
                    sessionId,
                    ex).ConfigureAwait(false);
                return false;
            }
        }

        opened = MaterializeAtCursor(opened, opened.HistoryCursor);

        long openingIntent;
        lock (gate)
        {
            if (!IsCurrentLocked(openingEpoch)) return false;
            session = opened;
            draftColorRecipe = null;
            draftTemplateEdit = null;
            previewLease = null;
            originalLease = null;
            colorBaseLease = null;
            colorBaseArtifactLease = null;
            colorBaseFingerprint = null;
            colorBaseTask = null;
            gpuColorPreviewAvailable = null;
            openingIntent = ++intentRevision;
            var currentMediaId = opened.CurrentMediaId ?? opened.Media.FirstOrDefault()?.Id;
            var currentRecipe = CloneRecipe(GetEffectiveColorRecipe(opened, currentMediaId))
                                ?? new WMColorRecipe { Name = "工作台调整" };
            var multiFrameDraft = NormalizeMultiFrameDraft(opened.MultiFrameConfiguration, opened.Media);
            var collageDraft = NormalizeCollageDraft(opened.CollageConfiguration, opened.Media);
            var stackCapability = imagingCapabilities?.Probe(
                multiFrameDraft.Mode == WMStackMode.StarTrail
                    ? WMImagingFeature.StarTrail
                    : WMImagingFeature.MultiFrame);
            state = new WMWorkspaceState
            {
                SessionId = opened.Id,
                Mode = opened.Mode,
                ReturnPath = opened.ReturnPath,
                Media = WMWorkspaceProjection.Media(opened),
                CurrentMediaId = currentMediaId,
                TemplateId = GetEffectiveTemplateId(opened, currentMediaId),
                TemplateEdit = GetEffectiveTemplateEdit(opened, currentMediaId),
                ColorRecipe = CloneRecipe(currentRecipe),
                ColorGradeTool = new WMColorGradeToolState(
                    CloneRecipe(currentRecipe)!,
                    colorPresetLibrary?.Load() ?? [],
                    null,
                    new WMColorReferenceState(null, null),
                    false),
                MultiFrameTool = new WMMultiFrameToolState(
                    multiFrameDraft,
                    stackCapability,
                    false),
                CollageTool = new WMCollageToolState(
                    collageDraft,
                    false),
                Activity = WMWorkspaceActivity.Idle,
                PanelSize = WMWorkspacePanelSize.Half,
                CanUndo = opened.HistoryCursor > 0,
                CanRedo = opened.HistoryCursor < opened.Transactions.Count,
                History = WMWorkspaceProjection.History(opened),
                HistoryCursor = opened.HistoryCursor,
                Recovery = openResult.Issues.Count > 0 ? openResult : null,
                ActiveJob = WMWorkspaceProjection.Job(opened.ActiveJobCheckpoint),
                TemplateDesign = opened.Mode == WMWorkspaceMode.TemplateDesign
                    ? new WMTemplateDesignToolState(null, false, false, 0, 1)
                    : null
            };
        }
        await RefreshMediaPreviewUrlsAsync(
            WMWorkspaceProjection.Media(opened),
            openingEpoch,
            notify: false,
            epochToken).ConfigureAwait(false);
        Changed.Invoke(State);
        if (opened.Mode != WMWorkspaceMode.TemplateDesign && opened.Media.Count > 0)
            await QueuePreviewTrackedAsync(opened, openingEpoch, openingIntent, epochToken).ConfigureAwait(false);
        var isCurrent = IsCurrent(openingEpoch);
        if (isCurrent)
        {
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Information,
                "workspace-open-completed",
                "工作台会话已打开。",
                opened.Id,
                properties: new Dictionary<string, string>
                {
                    ["mode"] = opened.Mode.ToString(),
                    ["mediaCount"] = opened.Media.Count.ToString(),
                    ["revision"] = opened.Revision.ToString(),
                    ["historyCursor"] = opened.HistoryCursor.ToString()
                }).ConfigureAwait(false);
        }
        return isCurrent;
    }

    public async Task<bool> RecoverAsync(
        WMWorkspaceRecoveryAction action,
        IReadOnlyList<string> affectedIds,
        CancellationToken cancellationToken = default)
    {
        string? sessionId;
        lock (gate) sessionId = state.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId)) return false;
        await recoveryService.RecoverAsync(sessionId, action, affectedIds, cancellationToken)
            .ConfigureAwait(false);
        if (action == WMWorkspaceRecoveryAction.DiscardSession)
        {
            await CloseAsync().ConfigureAwait(false);
            return false;
        }
        return await OpenAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    public Task SelectMediaAsync(string mediaId, CancellationToken cancellationToken = default) =>
        RunDurableCommandAsync(async context =>
        {
            if (!context.Session.Media.Any(item => item.Id == mediaId)
                || context.Session.CurrentMediaId == mediaId) return;
            var updated = NextRevision(context.Session with { CurrentMediaId = mediaId });
            var intent = CommitSession(context.Epoch, updated, clearColorDraft: false, value => value with
            {
                CurrentMediaId = mediaId,
                TemplateId = GetEffectiveTemplateId(updated, mediaId),
                TemplateEdit = GetEffectiveTemplateEdit(updated, mediaId),
                ColorRecipe = CloneRecipe(GetEffectiveColorRecipe(updated, mediaId)),
                ColorGradeTool = value.ColorGradeTool with
                {
                    Draft = CloneRecipe(GetEffectiveColorRecipe(updated, mediaId))
                            ?? new WMColorRecipe { Name = "工作台调整" }
                },
                IsComparingOriginal = false
            });
            await PersistAndPreviewAsync(updated, context.Epoch, intent, context.Token).ConfigureAwait(false);
        }, cancellationToken);

    public Task ToggleMediaSelectionAsync(string mediaId, CancellationToken cancellationToken = default) =>
        RunDurableCommandAsync(async context =>
        {
            var target = context.Session.Media.FirstOrDefault(item => item.Id == mediaId);
            if (target is null) return;
            var wasSelected = context.Session.SelectedMediaIds.Contains(mediaId, StringComparer.Ordinal)
                              || context.Session.SelectedMediaIds.Count == 0 && target.IsSelected;
            var selectedBefore = context.Session.SelectedMediaIds.ToHashSet(StringComparer.Ordinal);
            if (selectedBefore.Count == 0)
                selectedBefore.UnionWith(context.Session.Media
                    .Where(item => item.IsSelected)
                    .Select(item => item.Id));
            var media = context.Session.Media
                .Select(item => item with
                {
                    IsSelected = item.Id == mediaId
                        ? !wasSelected
                        : selectedBefore.Contains(item.Id)
                })
                .ToArray();
            var selected = media.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
            var updated = NextRevision(context.Session with
            {
                Media = media,
                SelectedMediaIds = selected
            });
            CommitPassiveSession(context.Epoch, updated, value => value with
            {
                Media = WMWorkspaceProjection.Media(updated)
            });
            await PersistAsync(updated, context.Epoch, context.Token).ConfigureAwait(false);
        }, cancellationToken);

    public Task ImportMediaAsync(
        IReadOnlyList<IWMPhotoImportSource> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0) return Task.CompletedTask;
        return RunDurableCommandAsync(async context =>
        {
            if (imageImporter is null || executionProfiles is null)
                throw new InvalidOperationException("当前宿主未注册工作台导入服务。");
            IReadOnlyList<WMWorkspaceMedia> imported;
            try
            {
                imported = await imageImporter.ImportAsync(
                    sources,
                    sessionStore.GetSessionDirectory(context.Session.Id),
                    executionProfiles.GetInteractiveProfile(),
                    cancellationToken: context.Token).ConfigureAwait(false);
            }
            finally
            {
                foreach (var source in sources)
                    await source.DisposeAsync().ConfigureAwait(false);
            }
            if (imported.Count == 0) return;

            var catalog = Catalog(context.Session).Concat(imported)
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var artifacts = context.Session.Artifacts
                .Concat(imported.Select(item => item.Artifact))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            var currentArtifacts = new Dictionary<string, string>(
                context.Session.CurrentArtifactIdsByMediaId, StringComparer.Ordinal);
            foreach (var media in imported) currentArtifacts[media.Id] = media.Artifact.Id;
            var selectedIds = context.Session.SelectedMediaIds
                .Concat(imported.Select(item => item.Id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var updated = AppendStructuralTransaction(
                context.Session with
                {
                    MediaCatalog = catalog,
                    Artifacts = artifacts,
                    CurrentArtifactIdsByMediaId = currentArtifacts,
                    SelectedMediaIds = selectedIds,
                    CurrentMediaId = context.Session.CurrentMediaId ?? imported[0].Id
                },
                $"导入 {imported.Count} 张素材",
                [],
                imported.Select(item => item.Id).ToArray(),
                []);
            updated = NextRevision(MaterializeAtCursor(updated, updated.HistoryCursor));
            var intent = CommitSession(context.Epoch, updated, clearColorDraft: true, value => value with
            {
                Media = WMWorkspaceProjection.Media(updated),
                CurrentMediaId = updated.CurrentMediaId,
                HasTransientEdits = false,
                TransientEditMode = null,
                Message = $"已导入 {imported.Count} 张素材"
            });
            await PersistAndPreviewAsync(updated, context.Epoch, intent, context.Token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task RemoveMediaAsync(
        IReadOnlyList<string> mediaIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaIds);
        return RunDurableCommandAsync(async context =>
        {
            var active = context.Session.ActiveMediaIds.ToHashSet(StringComparer.Ordinal);
            var removed = mediaIds.Where(active.Contains).Distinct(StringComparer.Ordinal).ToArray();
            if (removed.Length == 0) return;
            var updated = AppendStructuralTransaction(
                context.Session,
                removed.Length == 1 ? "移除素材" : $"移除 {removed.Length} 张素材",
                [],
                [],
                removed);
            updated = NextRevision(MaterializeAtCursor(updated, updated.HistoryCursor));
            var intent = CommitSession(context.Epoch, updated, clearColorDraft: true, value => value with
            {
                Media = WMWorkspaceProjection.Media(updated),
                CurrentMediaId = updated.CurrentMediaId,
                HasTransientEdits = false,
                TransientEditMode = null
            });
            await PersistAndPreviewAsync(updated, context.Epoch, intent, context.Token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<string> CreateDerivedMediaAsync(
        WMDerivedMediaRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateDerivedMediaCoreAsync(request, cancellationToken);
    }

    private async Task<string> CreateDerivedMediaCoreAsync(
        WMDerivedMediaRequest request,
        CancellationToken cancellationToken)
    {
        var derivedMediaId = Guid.NewGuid().ToString("N");
        using var operation = jobs.Begin(cancellationToken);
        try
        {
            await RunDurableCommandAsync(async context =>
            {
                if (derivedMediaProcessor is null)
                    throw new PlatformNotSupportedException("当前宿主未注册派生素材处理器。");
                var sourceIds = request.SourceMediaIds.Distinct(StringComparer.Ordinal).ToArray();
                if (sourceIds.Length < 2)
                    throw new InvalidOperationException("派生素材至少需要两张源图片。");
                var sourceById = Catalog(context.Session).ToDictionary(item => item.Id, StringComparer.Ordinal);
                if (sourceIds.Any(id => !sourceById.ContainsKey(id)))
                    throw new InvalidOperationException("派生素材的源图片已不存在。");
                var sources = sourceIds.Select(id => sourceById[id]).ToArray();
                var inputs = sources.Select(item => ResolveArtifact(context.Session, item)).ToArray();
                var now = DateTime.UtcNow;
                var checkpoint = new WMWorkspaceJobCheckpoint(
                    Guid.NewGuid().ToString("N"),
                    WMWorkspaceJobKind.DerivedMedia,
                    WMWorkspaceJobStatus.Running,
                    System.Text.Json.JsonSerializer.Serialize(request),
                    [],
                    now,
                    now);
                var working = NextRevision(context.Session with { ActiveJobCheckpoint = checkpoint });
                CommitPassiveSession(context.Epoch, working, value => value with
                {
                    ActiveJob = WMWorkspaceProjection.Job(checkpoint),
                    CollageTool = value.CollageTool with { IsBusy = true },
                    Message = "正在生成拼图…",
                    CanCancel = true
                });
                await PersistAsync(working, context.Epoch, context.Token).ConfigureAwait(false);

                WMDerivedMediaOutput generated;
                try
                {
                    generated = await derivedMediaProcessor.ExecuteAsync(
                        request,
                        inputs,
                        sessionStore.GetSessionDirectory(context.Session.Id),
                        context.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var status = ex is OperationCanceledException
                        ? WMWorkspaceJobStatus.Canceled
                        : WMWorkspaceJobStatus.Failed;
                    var failedCheckpoint = checkpoint with
                    {
                        Status = status,
                        UpdatedAtUtc = DateTime.UtcNow,
                        ErrorMessage = ex.Message
                    };
                    var failed = NextRevision(working with { ActiveJobCheckpoint = failedCheckpoint });
                    CommitPassiveSession(context.Epoch, failed, value => value with
                    {
                        ActiveJob = WMWorkspaceProjection.Job(failedCheckpoint),
                        CollageTool = value.CollageTool with { IsBusy = false },
                        CanCancel = false
                    });
                    await PersistAsync(failed, context.Epoch, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }

                var artifact = generated.Artifact;
                if (!File.Exists(artifact.FilePath))
                    throw new FileNotFoundException("派生素材产物不存在。", artifact.FilePath);
                var source = sources[0];
                var media = new WMWorkspaceMedia
                {
                    Id = derivedMediaId,
                    DisplayName = string.IsNullOrWhiteSpace(request.SuggestedFileName)
                        ? generated.SuggestedFileName
                        : request.SuggestedFileName,
                    OriginalReference = source.OriginalReference,
                    Artifact = artifact,
                    IsSelected = request.SelectResult
                };
                var currentArtifacts = new Dictionary<string, string>(
                    working.CurrentArtifactIdsByMediaId, StringComparer.Ordinal)
                {
                    [derivedMediaId] = artifact.Id
                };
                var selected = request.SelectResult
                    ? working.SelectedMediaIds.Append(derivedMediaId).Distinct(StringComparer.Ordinal).ToArray()
                    : working.SelectedMediaIds;
                var assignments = new[]
                {
                    new WMWorkspaceOperationAssignment([derivedMediaId], [generated.Operation])
                };
                var completedCheckpoint = checkpoint with
                {
                    Status = WMWorkspaceJobStatus.Completed,
                    StableArtifactIds = [artifact.Id],
                    UpdatedAtUtc = DateTime.UtcNow
                };
                var updated = AppendStructuralTransaction(
                    working with
                    {
                        MediaCatalog = Catalog(working).Append(media).ToArray(),
                        Artifacts = working.Artifacts.Append(artifact)
                            .GroupBy(item => item.Id, StringComparer.Ordinal)
                            .Select(group => group.Last())
                            .ToArray(),
                        CurrentArtifactIdsByMediaId = currentArtifacts,
                        SelectedMediaIds = selected,
                        CurrentMediaId = request.SelectResult ? derivedMediaId : working.CurrentMediaId,
                        ActiveJobCheckpoint = completedCheckpoint
                    },
                    request.Label,
                    assignments,
                    [derivedMediaId],
                    []);
                updated = NextRevision(MaterializeAtCursor(updated, updated.HistoryCursor));
                var intent = CommitSession(context.Epoch, updated, clearColorDraft: true, value => value with
                {
                    Media = WMWorkspaceProjection.Media(updated),
                    CurrentMediaId = updated.CurrentMediaId,
                    HasTransientEdits = value.TransientEditMode == WMWorkspaceMode.Collage,
                    TransientEditMode = value.TransientEditMode == WMWorkspaceMode.Collage
                        ? WMWorkspaceMode.Collage
                        : null,
                    ActiveJob = WMWorkspaceProjection.Job(completedCheckpoint),
                    CollageTool = value.CollageTool with { IsBusy = false },
                    CanCancel = false,
                    Message = "拼图已生成"
                });
                await PersistAndPreviewAsync(updated, context.Epoch, intent, context.Token).ConfigureAwait(false);
                await RecordTraceAsync(
                    updated.Id,
                    artifact.ContentHash,
                    checkpoint.Id,
                    "derived-media-completed",
                    cacheHit: false,
                    canceled: false,
                    errorCode: null).ConfigureAwait(false);
            }, operation.Token).ConfigureAwait(false);
            return derivedMediaId;
        }
        finally
        {
            jobs.Complete(operation);
            UpdateCurrent(value => value with { CollageTool = value.CollageTool with { IsBusy = false } });
        }
    }

    public Task SetModeAsync(WMWorkspaceMode mode, CancellationToken cancellationToken = default) =>
        RunDurableCommandAsync(async context =>
        {
            if (context.Session.Mode == mode) return;
            WMColorRecipe? pendingColor;
            WMApplyScope pendingScope;
            lock (gate)
            {
                pendingColor = state.HasTransientEdits && draftColorRecipe is not null
                    ? NormalizeRecipe(draftColorRecipe)
                    : null;
                pendingScope = state.ApplyScope;
            }
            var updated = context.Session;
            if (pendingColor is not null)
            {
                var targetIds = ResolveTargetMediaIds(updated, pendingScope);
                if (targetIds.Count == 0)
                {
                    updated = updated with { ColorRecipe = CloneRecipe(pendingColor) };
                }
                else
                {
                    var overrides = updated.ColorRecipesByMediaId.ToDictionary(
                        pair => pair.Key,
                        pair => CloneRecipe(pair.Value),
                        StringComparer.Ordinal);
                    foreach (var mediaId in targetIds) overrides[mediaId] = CloneRecipe(pendingColor);
                    updated = AppendTransaction(
                        updated with { ColorRecipesByMediaId = overrides },
                        "调整颜色",
                        WMImageOperationKind.ColorGrade,
                        targetIds,
                        pendingColor);
                }
            }
            updated = NextRevision(updated with { Mode = mode });
            var intent = CommitSession(context.Epoch, updated, clearColorDraft: pendingColor is not null, value => value with
            {
                Mode = mode,
                ColorRecipe = pendingColor is null
                    ? value.ColorRecipe
                    : CloneRecipe(GetEffectiveColorRecipe(updated, updated.CurrentMediaId)),
                ColorGradeTool = pendingColor is null
                    ? value.ColorGradeTool
                    : value.ColorGradeTool with
                    {
                        Draft = CloneRecipe(GetEffectiveColorRecipe(updated, updated.CurrentMediaId))
                                ?? new WMColorRecipe { Name = "工作台调整" },
                        IsBusy = false
                    },
                HasTransientEdits = pendingColor is null && value.HasTransientEdits,
                TransientEditMode = pendingColor is null ? value.TransientEditMode : null,
                ErrorMessage = null,
                IsComparingOriginal = false
            });
            await PersistAsync(updated, context.Epoch, context.Token).ConfigureAwait(false);
            if (pendingColor is not null)
                await QueuePreviewTrackedAsync(updated, context.Epoch, intent, context.Token).ConfigureAwait(false);
        }, cancellationToken);

    public Task ApplyTemplateAsync(string? templateId, CancellationToken cancellationToken = default) =>
        ApplyTemplateAsync(templateId, WMApplyScope.Selected, cancellationToken);

    public Task ApplyTemplateAsync(
        string? templateId,
        WMApplyScope scope,
        CancellationToken cancellationToken = default) =>
        CommitTemplateAsync(
            new WMWorkspaceTemplateEdit(templateId, null),
            scope,
            cancellationToken);

    public async Task PreviewTemplateAsync(
        WMWorkspaceTemplateEdit edit,
        WMApplyScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit = await ResolveTemplatePreviewEditAsync(edit, cancellationToken).ConfigureAwait(false);
        WMWorkspaceSession previewSession;
        long currentEpoch;
        long intent;
        lock (gate)
        {
            if (closed || session is null || epochCancellation is null)
                throw new InvalidOperationException("工作台会话尚未打开。");
            currentEpoch = epoch;
            var targetIds = ResolveTargetMediaIds(session, scope);
            if (targetIds.Count == 0) return;
            var overrides = new Dictionary<string, string?>(
                session.TemplateIdsByMediaId,
                StringComparer.Ordinal);
            foreach (var mediaId in targetIds) overrides[mediaId] = edit.TemplateId;
            previewSession = AppendTransaction(
                session with { TemplateIdsByMediaId = overrides },
                "预览模板",
                WMImageOperationKind.Template,
                targetIds,
                new WMWorkspaceTemplateSelection(edit.TemplateId, edit.CanvasJson));
            draftTemplateEdit = edit;
            intent = ++intentRevision;
            state = state with
            {
                TemplateId = edit.TemplateId,
                TemplateEdit = edit,
                HasTransientEdits = true,
                TransientEditMode = WMWorkspaceMode.Template,
                IsComparingOriginal = false
            };
        }
        Changed.Invoke(State);
        await QueuePreviewTrackedAsync(
            previewSession, currentEpoch, intent, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WMWorkspaceTemplateEdit> ResolveTemplatePreviewEditAsync(
        WMWorkspaceTemplateEdit edit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(edit.TemplateId)
            || !string.IsNullOrWhiteSpace(edit.CanvasJson)) return edit;
        var canvas = await Global.GetCanvas(edit.TemplateId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("所选模板已不存在。");
        return new WMWorkspaceTemplateEdit(edit.TemplateId, Global.CanvasSerialize(canvas));
    }

    public Task CommitTemplateAsync(
        WMWorkspaceTemplateEdit edit,
        WMApplyScope scope,
        CancellationToken cancellationToken = default) =>
        RunDurableCommandAsync(async context =>
        {
            ArgumentNullException.ThrowIfNull(edit);
            var templateId = edit.TemplateId;
            var targetIds = ResolveTargetMediaIds(context.Session, scope);
            if (targetIds.Count == 0) return;
            var overrides = new Dictionary<string, string?>(
                context.Session.TemplateIdsByMediaId, StringComparer.Ordinal);
            var changed = false;
            foreach (var mediaId in targetIds)
            {
                if (string.Equals(GetEffectiveTemplateId(context.Session, mediaId), templateId, StringComparison.Ordinal))
                    continue;
                overrides[mediaId] = templateId;
                changed = true;
            }
            if (!changed && string.IsNullOrWhiteSpace(edit.CanvasJson)) return;
            string? canvasJson = null;
            if (!string.IsNullOrWhiteSpace(templateId))
            {
                var canvas = string.IsNullOrWhiteSpace(edit.CanvasJson)
                    ? await Global.GetCanvas(templateId).WaitAsync(context.Token).ConfigureAwait(false)
                    : Global.ReadConfig(edit.CanvasJson);
                if (canvas is null) throw new InvalidOperationException("所选模板已不存在。");
                canvasJson = await templateSnapshots.CreateAsync(
                    context.Session, templateId, canvas, context.Token).ConfigureAwait(false);
            }
            var updated = context.Session with
            {
                TemplateId = context.Session.TemplateId,
                TemplateIdsByMediaId = overrides
            };
            updated = AppendTransaction(
                updated,
                templateId is null ? "移除模板" : "应用模板",
                WMImageOperationKind.Template,
                targetIds,
                new WMWorkspaceTemplateSelection(templateId, canvasJson));
            updated = NextRevision(updated);
            var currentTemplate = GetEffectiveTemplateId(updated, updated.CurrentMediaId);
            var intent = CommitSession(context.Epoch, updated, clearColorDraft: false, value => value with
            {
                TemplateId = currentTemplate,
                TemplateEdit = GetEffectiveTemplateEdit(updated, updated.CurrentMediaId),
                HasTransientEdits = false,
                TransientEditMode = null,
                IsComparingOriginal = false
            });
            lock (gate) draftTemplateEdit = null;
            await PersistAndPreviewAsync(updated, context.Epoch, intent, context.Token).ConfigureAwait(false);
        }, cancellationToken);

    public Task DiscardTransientEditsAsync(CancellationToken cancellationToken = default)
    {
        WMWorkspaceSession current;
        WMWorkspaceMode? transientMode;
        long currentEpoch;
        long intent;
        lock (gate)
        {
            if (closed || session is null || epochCancellation is null) return Task.CompletedTask;
            current = session;
            transientMode = state.TransientEditMode;
            currentEpoch = epoch;
            draftColorRecipe = null;
            draftTemplateEdit = null;
            intent = ++intentRevision;
            state = state with
            {
                TemplateId = GetEffectiveTemplateId(current, current.CurrentMediaId),
                TemplateEdit = GetEffectiveTemplateEdit(current, current.CurrentMediaId),
                ColorRecipe = CloneRecipe(GetEffectiveColorRecipe(current, current.CurrentMediaId)),
                ColorGradeTool = state.ColorGradeTool with
                {
                    Draft = CloneRecipe(GetEffectiveColorRecipe(current, current.CurrentMediaId))
                            ?? new WMColorRecipe { Name = "工作台调整" }
                },
                MultiFrameTool = state.MultiFrameTool with
                {
                    Draft = NormalizeMultiFrameDraft(current.MultiFrameConfiguration, current.Media)
                },
                CollageTool = state.CollageTool with
                {
                    Draft = NormalizeCollageDraft(current.CollageConfiguration, current.Media)
                },
                HasTransientEdits = false,
                TransientEditMode = null,
                IsComparingOriginal = false
            };
        }
        Changed.Invoke(State);
        return current.Media.Count == 0
               || transientMode is WMWorkspaceMode.MultiFrame or WMWorkspaceMode.Collage
            ? Task.CompletedTask
            : QueuePreviewTrackedAsync(current, currentEpoch, intent, cancellationToken);
    }

    public Task UpdateColorGradeAsync(
        WMColorRecipe recipe,
        bool createHistoryEntry,
        CancellationToken cancellationToken = default)
        => UpdateColorGradeAsync(recipe, createHistoryEntry, WMApplyScope.Selected, cancellationToken);

    public Task UpdateColorGradeAsync(
        WMColorRecipe recipe,
        bool createHistoryEntry,
        WMApplyScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var normalized = NormalizeRecipe(recipe);
        return createHistoryEntry
            ? CommitColorGradeAsync(normalized, scope, cancellationToken)
            : PreviewColorGradeAsync(normalized, scope, cancellationToken);
    }

    public void SetApplyScope(WMApplyScope scope) =>
        UpdateCurrent(value => value with { ApplyScope = scope });

    public Task UpdateColorDraftAsync(
        WMColorRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var normalized = NormalizeRecipe(recipe);
        CancellationTokenSource? debounce = null;
        long currentEpoch;
        long currentIntent = 0;
        var useGpu = false;
        lock (gate)
        {
            if (closed || session is null || epochCancellation is null)
                throw new InvalidOperationException("工作台会话尚未打开。");
            currentEpoch = epoch;
            useGpu = colorPipelineCompiler is not null && gpuColorPreviewAvailable == true;
            if (useGpu)
                currentIntent = ++intentRevision;
            else
                debounce = transientPreviews.ReplaceDebounce(
                    cancellationToken,
                    epochCancellation.Token);
            draftColorRecipe = CloneRecipe(normalized);
            state = state with
            {
                ColorRecipe = CloneRecipe(normalized),
                ColorGradeTool = state.ColorGradeTool with
                {
                    Draft = CloneRecipe(normalized)!,
                    IsBusy = false
                },
                HasTransientEdits = true,
                TransientEditMode = WMWorkspaceMode.ColorGrade
            };
        }
        Changed.Invoke(State);
        var task = useGpu
            ? PreviewColorDraftGpuAsync(normalized, currentEpoch, currentIntent, cancellationToken)
            : PreviewColorDraftAfterDelayAsync(normalized, debounce!);
        TrackOwned(task);
        return Task.CompletedTask;
    }

    public Task SetColorPreviewCapabilityAsync(
        WMColorPreviewCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capability);
        WMColorRecipe? fallbackRecipe = null;
        WMApplyScope fallbackScope = WMApplyScope.Current;
        lock (gate)
        {
            gpuColorPreviewAvailable = capability.Supported && capability.Validated;
            if (!gpuColorPreviewAvailable.Value
                && state.HasTransientEdits
                && state.Mode == WMWorkspaceMode.ColorGrade)
            {
                fallbackRecipe = CloneRecipe(draftColorRecipe ?? state.ColorGradeTool.Draft);
                fallbackScope = state.ApplyScope;
            }
            state = state with
            {
                PreviewPresentation = state.PreviewPresentation with
                {
                    Backend = gpuColorPreviewAvailable.Value
                        ? WMInteractivePreviewBackend.WebGl2
                        : WMInteractivePreviewBackend.CpuSkia,
                    FallbackReason = capability.Reason
                }
            };
        }
        Changed.Invoke(State);
        var diagnostic = RecordLogSafeAsync(
            capability.Supported && capability.Validated
                ? WMDiagnosticLogLevel.Information
                : WMDiagnosticLogLevel.Warning,
            "workspace-color-preview-backend",
            capability.Supported && capability.Validated
                ? "实时调色已启用WebGL2。"
                : "实时调色已降级到CPU。",
            State.SessionId,
            properties: new Dictionary<string, string>
            {
                ["backend"] = capability.Supported && capability.Validated ? "WebGl2" : "CpuSkia",
                ["renderer"] = capability.Renderer ?? "unknown",
                ["pipelineVersion"] = capability.PipelineVersion.ToString(),
                ["validated"] = capability.Validated.ToString(),
                ["averageDeltaE"] = capability.AverageDeltaE?.ToString("0.###") ?? string.Empty,
                ["maximumDeltaE"] = capability.MaximumDeltaE?.ToString("0.###") ?? string.Empty,
                ["fallbackReason"] = capability.Reason ?? string.Empty
            });
        return fallbackRecipe is null
            ? diagnostic
            : Task.WhenAll(
                diagnostic,
                PreviewColorGradeAsync(fallbackRecipe, fallbackScope, cancellationToken));
    }

    public Task RecordColorPreviewPerformanceAsync(WMColorPreviewPerformanceSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        string? sessionId;
        lock (gate) sessionId = state.SessionId;
        return RecordLogSafeAsync(
            WMDiagnosticLogLevel.Debug,
            "workspace-color-preview-performance",
            $"WebGL调色{sample.Stage}耗时 {sample.DurationMilliseconds:0.###} ms。",
            sessionId,
            properties: new Dictionary<string, string>
            {
                ["backend"] = "WebGl2",
                ["stage"] = sample.Stage,
                ["durationMs"] = sample.DurationMilliseconds.ToString("0.###"),
                ["detail"] = sample.Detail ?? string.Empty
            });
    }

    private async Task PreviewColorDraftAfterDelayAsync(
        WMColorRecipe recipe,
        CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(75, debounce.Token).ConfigureAwait(false);
            WMApplyScope scope;
            lock (gate) scope = state.ApplyScope;
            await PreviewColorGradeAsync(recipe, scope, debounce.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            transientPreviews.Complete(debounce);
            debounce.Dispose();
        }
    }

    private async Task PreviewColorDraftGpuAsync(
        WMColorRecipe recipe,
        long currentEpoch,
        long currentIntent,
        CancellationToken cancellationToken)
    {
        if (colorPipelineCompiler is null) return;
        WMWorkspaceSession snapshot;
        WMWorkspaceMedia media;
        CancellationToken epochToken;
        lock (gate)
        {
            if (!IsCurrentLocked(currentEpoch, currentIntent) || session is null || epochCancellation is null) return;
            snapshot = session;
            epochToken = epochCancellation.Token;
            media = snapshot.Media.FirstOrDefault(item => item.Id == snapshot.CurrentMediaId)
                    ?? snapshot.Media.FirstOrDefault()
                    ?? throw new InvalidOperationException("工作台没有可调色素材。");
            media = media with { Artifact = ResolveArtifact(snapshot, media) };
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, epochToken);
        try
        {
            var basePlan = await renderPlanCompiler.CompileAsync(
                snapshot,
                media.Id,
                WMRenderTarget.InteractiveBase(),
                linked.Token).ConfigureAwait(false);
            var basePreviewTask = GetOrCreateColorBaseAsync(
                snapshot, media, basePlan, currentEpoch, linked.Token);
            var programTask = colorPipelineCompiler.CompileAsync(
                basePlan.BaseArtifact, recipe, linked.Token);
            await Task.WhenAll(basePreviewTask, programTask).ConfigureAwait(false);
            var basePreview = await basePreviewTask.ConfigureAwait(false);
            var program = await programTask.ConfigureAwait(false);
            if (!IsCurrent(currentEpoch, currentIntent)) return;
            var version = Interlocked.Increment(ref nextBlobVersion);
            TryUpdate(currentEpoch, currentIntent, value => value with
            {
                Activity = value.Activity == WMWorkspaceActivity.Previewing
                    ? WMWorkspaceActivity.PreviewReady
                    : value.Activity,
                PreviewPresentation = new WMWorkspacePreviewPresentation(
                    version,
                    basePreview.Fingerprint,
                    basePreview.Url,
                    value.PreviewUrl,
                    program,
                    WMInteractivePreviewBackend.WebGl2,
                    IsSettled: false,
                    FallbackReason: null)
            });
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Information,
                "workspace-gpu-preview-updated",
                "GPU调色参数已更新。",
                snapshot.Id,
                properties: new Dictionary<string, string>
                {
                    ["backend"] = "WebGl2",
                    ["programFingerprint"] = program.ProgramFingerprint,
                    ["baseFingerprint"] = basePreview.Fingerprint
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            lock (gate) gpuColorPreviewAvailable = false;
            TryUpdate(currentEpoch, currentIntent, value => value with
            {
                PreviewPresentation = value.PreviewPresentation with
                {
                    Backend = WMInteractivePreviewBackend.CpuSkia,
                    FallbackReason = ex.Message
                }
            });
            await PreviewColorGradeAsync(recipe, State.ApplyScope, linked.Token).ConfigureAwait(false);
        }
    }

    private async Task<ColorBasePreview> GetOrCreateColorBaseAsync(
        WMWorkspaceSession snapshot,
        WMWorkspaceMedia media,
        WMCompiledRenderPlan plan,
        long currentEpoch,
        CancellationToken cancellationToken)
    {
        var fingerprint = await previewService.CreateFingerprintAsync(plan, cancellationToken)
            .ConfigureAwait(false);
        Task<ColorBasePreview> resultTask;
        lock (gate)
        {
            if (!IsCurrentLocked(currentEpoch))
                throw new OperationCanceledException(cancellationToken);
            if (string.Equals(colorBaseFingerprint, fingerprint, StringComparison.Ordinal))
            {
                if (colorBaseTask is not null) resultTask = colorBaseTask;
                else if (state.PreviewPresentation.BaseUrl is { Length: > 0 } existing)
                    resultTask = Task.FromResult(new ColorBasePreview(fingerprint, existing));
                else resultTask = CreateColorBaseTask();
            }
            else
            {
                colorBaseFingerprint = fingerprint;
                resultTask = CreateColorBaseTask();
            }

            colorBaseTask = resultTask;
        }
        return await resultTask.ConfigureAwait(false);

        Task<ColorBasePreview> CreateColorBaseTask()
        {
            var committedColor = GetEffectiveColorRecipe(snapshot, media.Id);
            if (committedColor is null && state.PreviewUrl is { Length: > 0 } stable)
                return Task.FromResult(new ColorBasePreview(fingerprint, stable));
            return PrepareColorBaseAsync(
                snapshot, media, plan, fingerprint, currentEpoch, cancellationToken);
        }
    }

    private async Task<ColorBasePreview> PrepareColorBaseAsync(
        WMWorkspaceSession snapshot,
        WMWorkspaceMedia media,
        WMCompiledRenderPlan plan,
        string fingerprint,
        long currentEpoch,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var version = Interlocked.Increment(ref nextBlobVersion);
        IDisposable? candidateArtifactLease = null;
        try
        {
            var preview = await previewService.RenderAsync(
                plan, version, cancellationToken).ConfigureAwait(false);
            candidateArtifactLease = artifactCache.AcquireLease(
                ResolveSessionDirectory(plan.BaseArtifact), fingerprint);
            await using var content = new FileStream(
                preview.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var lease = await objectUrls.PublishAsync(
                ColorBaseOwner, version, content, preview.MimeType, cancellationToken).ConfigureAwait(false);
            if (lease is null) throw new OperationCanceledException(cancellationToken);

            IDisposable? previousArtifactLease;
            lock (gate)
            {
                if (!IsCurrentLocked(currentEpoch)
                    || !string.Equals(colorBaseFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    previousArtifactLease = null;
                }
                else
                {
                    colorBaseLease = lease;
                    previousArtifactLease = colorBaseArtifactLease;
                    colorBaseArtifactLease = candidateArtifactLease;
                    candidateArtifactLease = null;
                }
            }
            if (!IsCurrent(currentEpoch)
                || !string.Equals(colorBaseFingerprint, fingerprint, StringComparison.Ordinal))
            {
                await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
                throw new OperationCanceledException(cancellationToken);
            }
            previousArtifactLease?.Dispose();
            stopwatch.Stop();
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Information,
                "workspace-color-base-ready",
                "实时调色基础图已准备。",
                snapshot.Id,
                properties: new Dictionary<string, string>
                {
                    ["baseFingerprint"] = fingerprint,
                    ["elapsedMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                    ["cacheHit"] = preview.CacheHit.ToString(),
                    ["width"] = preview.Width.ToString(),
                    ["height"] = preview.Height.ToString()
                }).ConfigureAwait(false);
            return new ColorBasePreview(fingerprint, lease.Url);
        }
        catch
        {
            lock (gate)
            {
                if (string.Equals(colorBaseFingerprint, fingerprint, StringComparison.Ordinal))
                    colorBaseTask = null;
            }
            throw;
        }
        finally
        {
            candidateArtifactLease?.Dispose();
        }
    }

    public Task CommitColorDraftAsync(
        WMApplyScope scope,
        CancellationToken cancellationToken = default)
    {
        WMColorRecipe recipe;
        lock (gate)
        {
            transientPreviews.Cancel();
            recipe = CloneRecipe(draftColorRecipe ?? state.ColorGradeTool.Draft)!;
        }
        return CommitColorGradeAsync(NormalizeRecipe(recipe), scope, cancellationToken);
    }

    public Task ImportColorReferenceAsync(
        IWMPhotoImportSource source,
        CancellationToken cancellationToken = default) =>
        ImportColorReferenceCoreAsync(source, commit: true, cancellationToken);

    public Task ImportColorReferenceDraftAsync(
        IWMPhotoImportSource source,
        CancellationToken cancellationToken = default) =>
        ImportColorReferenceCoreAsync(source, commit: false, cancellationToken);

    private async Task ImportColorReferenceCoreAsync(
        IWMPhotoImportSource source,
        bool commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (colorReferenceService is null)
            throw new PlatformNotSupportedException("当前宿主未注册参考图导入服务。");
        string sessionId;
        long currentEpoch;
        lock (gate)
        {
            sessionId = session?.Id ?? throw new InvalidOperationException("工作台会话尚未打开。");
            currentEpoch = epoch;
            state = state with
            {
                ColorGradeTool = state.ColorGradeTool with { IsBusy = true }
            };
        }
        Changed.Invoke(State);
        try
        {
            var imported = await colorReferenceService.ImportAsync(
                sessionId, source, cancellationToken).ConfigureAwait(false);
            await PublishColorReferenceAsync(imported, currentEpoch, cancellationToken).ConfigureAwait(false);
            var recipe = new WMColorRecipe
            {
                Name = Path.GetFileNameWithoutExtension(imported.DisplayName),
                ReferenceProfile = imported.Profile,
                ReferenceMapping = new WMColorReferenceMappingSettings
                {
                    IsConfigured = true,
                    Enabled = true,
                    Strength = 55,
                    MatchMode = WMColorMappingMode.Natural
                },
                Grade = new WMColorGradeSettings()
            };
            await UpdateColorDraftAsync(recipe, cancellationToken).ConfigureAwait(false);
            if (commit)
                await CommitColorDraftAsync(State.ApplyScope, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryUpdate(currentEpoch, null, value => value with
            {
                ColorGradeTool = value.ColorGradeTool with { IsBusy = false }
            });
        }
    }

    public Task ClearColorReferenceAsync(CancellationToken cancellationToken = default) =>
        ClearColorReferenceCoreAsync(commit: true, cancellationToken);

    public Task ClearColorReferenceDraftAsync(CancellationToken cancellationToken = default) =>
        ClearColorReferenceCoreAsync(commit: false, cancellationToken);

    private async Task ClearColorReferenceCoreAsync(
        bool commit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WMObjectUrlLease? lease;
        WMColorRecipe recipe;
        lock (gate)
        {
            lease = colorReferenceLease;
            colorReferenceLease = null;
            colorReferencePath = null;
            colorReferenceName = null;
            recipe = CloneRecipe(draftColorRecipe ?? state.ColorGradeTool.Draft)!;
            recipe.ReferenceProfile = null;
            recipe.ReferenceMapping.Enabled = false;
            recipe.GeneratedBaseGrade = null;
            state = state with
            {
                ColorGradeTool = state.ColorGradeTool with
                {
                    Draft = recipe,
                    Reference = new WMColorReferenceState(null, null)
                }
            };
        }
        if (lease is not null) await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
        if (commit)
            await CommitColorGradeAsync(recipe, State.ApplyScope, cancellationToken).ConfigureAwait(false);
        else
            await UpdateColorDraftAsync(recipe, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveColorPresetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (colorPresetLibrary is null)
            throw new PlatformNotSupportedException("当前宿主未注册调色预设服务。");
        WMColorRecipe recipe;
        lock (gate)
        {
            recipe = CloneRecipe(draftColorRecipe ?? state.ColorGradeTool.Draft)!;
            if (!string.IsNullOrWhiteSpace(name)) recipe.Name = name.Trim();
        }
        await colorPresetLibrary.SaveAsync(recipe, colorReferencePath, cancellationToken)
            .ConfigureAwait(false);
        RefreshColorPresets(recipe.Id);
    }

    public Task DeleteColorPresetAsync(
        string presetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (colorPresetLibrary is null)
            throw new PlatformNotSupportedException("当前宿主未注册调色预设服务。");
        colorPresetLibrary.Delete(presetId);
        RefreshColorPresets(null);
        return Task.CompletedTask;
    }

    public async Task ApplyColorPresetAsync(
        string presetId,
        CancellationToken cancellationToken = default)
    {
        var preset = State.ColorGradeTool.Presets.FirstOrDefault(item => item.Id == presetId)
                     ?? throw new KeyNotFoundException("调色预设不存在。");
        lock (gate)
            state = state with
            {
                ColorGradeTool = state.ColorGradeTool with { SelectedPresetId = presetId }
            };
        Changed.Invoke(State);
        await UpdateColorDraftAsync(preset, cancellationToken).ConfigureAwait(false);
        await CommitColorDraftAsync(State.ApplyScope, cancellationToken).ConfigureAwait(false);
    }

    public void ClearStatusMessage() =>
        UpdateCurrent(value => value with { ErrorMessage = null, Message = string.Empty });

    public void ClearExportResults() =>
        UpdateCurrent(value => value with
        {
            ExportTool = value.ExportTool with { Results = [] },
            ActiveJob = value.ActiveJob.Kind == WMWorkspaceJobKind.Export
                ? WMWorkspaceJobState.Idle
                : value.ActiveJob
        });

    public void SetPanelSize(WMWorkspacePanelSize size) =>
        UpdateCurrent(value => value with { PanelSize = size });

    public void UpdateTemplateDesignState(
        string? selectedControlId,
        bool isDirty,
        bool isGestureActive,
        int historyCursor,
        int historyCount) =>
        UpdateCurrent(value => value with
        {
            TemplateDesign = new WMTemplateDesignToolState(
                selectedControlId,
                isDirty,
                isGestureActive,
                historyCursor,
                historyCount)
        });

    public Task UpdateMultiFrameDraftAsync(
        WMMultiFrameDraft draft,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capability = imagingCapabilities?.Probe(
            draft.Mode == WMStackMode.StarTrail
                ? WMImagingFeature.StarTrail
                : WMImagingFeature.MultiFrame);
        UpdateCurrent(value => value with
        {
            MultiFrameTool = value.MultiFrameTool with
            {
                Draft = draft with
                {
                    Roles = new Dictionary<string, WMFrameRole?>(draft.Roles, StringComparer.Ordinal)
                },
                Capability = capability
            },
            HasTransientEdits = true,
            TransientEditMode = WMWorkspaceMode.MultiFrame
        });
        return Task.CompletedTask;
    }

    public Task CommitMultiFrameDraftAsync(CancellationToken cancellationToken = default) =>
        RunDurableCommandAsync(async context =>
        {
            WMMultiFrameDraft draft;
            lock (gate) draft = CopyMultiFrameDraft(state.MultiFrameTool.Draft);
            draft = NormalizeMultiFrameDraft(draft, context.Session.Media);
            var updated = NextRevision(context.Session with { MultiFrameConfiguration = draft });
            CommitSession(context.Epoch, updated, clearColorDraft: false, value => value with
            {
                MultiFrameTool = value.MultiFrameTool with { Draft = draft },
                HasTransientEdits = false,
                TransientEditMode = null
            });
            await PersistAsync(updated, context.Epoch, context.Token).ConfigureAwait(false);
        }, cancellationToken);

    public Task UpdateCollageDraftAsync(
        WMCollageDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();
        var available = State.Media.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var ordered = draft.OrderedMediaIds
            .Where(available.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        UpdateCurrent(value => value with
        {
            CollageTool = value.CollageTool with
            {
                Draft = draft with { OrderedMediaIds = ordered }
            },
            HasTransientEdits = true,
            TransientEditMode = WMWorkspaceMode.Collage
        });
        return Task.CompletedTask;
    }

    public Task CommitCollageDraftAsync(CancellationToken cancellationToken = default) =>
        RunDurableCommandAsync(async context =>
        {
            WMCollageDraft draft;
            lock (gate) draft = CopyCollageDraft(state.CollageTool.Draft);
            draft = NormalizeCollageDraft(draft, context.Session.Media);
            var updated = NextRevision(context.Session with { CollageConfiguration = draft });
            CommitSession(context.Epoch, updated, clearColorDraft: false, value => value with
            {
                CollageTool = value.CollageTool with { Draft = draft },
                HasTransientEdits = false,
                TransientEditMode = null
            });
            await PersistAsync(updated, context.Epoch, context.Token).ConfigureAwait(false);
        }, cancellationToken);

    public Task<string> ExecuteCollageAsync(CancellationToken cancellationToken = default)
    {
        var draft = State.CollageTool.Draft;
        if (draft.OrderedMediaIds.Count < 2)
            throw new InvalidOperationException("拼图至少需要两张素材。");
        return CreateDerivedMediaAsync(
            new WMDerivedMediaRequest(
                WMDerivedMediaKind.Collage,
                draft.OrderedMediaIds,
                "创建拼图",
                new WMCollageSettings(
                    draft.OrderedMediaIds,
                    draft.Direction,
                    0,
                    "#FFFFFF"),
                $"拼图-{DateTime.Now:yyyyMMdd-HHmmss}.png",
                true),
            cancellationToken);
    }

    public Task ExecuteMultiFrameAsync(CancellationToken cancellationToken = default)
    {
        var draft = State.MultiFrameTool.Draft;
        var lights = draft.Roles.Where(pair => pair.Value == WMFrameRole.Light)
            .Select(pair => pair.Key).ToArray();
        var darks = draft.Roles.Where(pair => pair.Value == WMFrameRole.Dark)
            .Select(pair => pair.Key).ToArray();
        var settings = WMMultiFrameStackSettings.CreateDefault(draft.Mode) with
        {
            NormalizeExposure = draft.NormalizeExposure,
            RepairHotPixels = draft.RepairHotPixels,
            AutoCrop = draft.AutoCrop
        };
        return ExecuteMultiFrameAsync(settings, lights, darks, cancellationToken);
    }

    public Task ExecuteMultiFrameAsync(
        WMMultiFrameStackSettings settings,
        IReadOnlyList<string> lightMediaIds,
        IReadOnlyList<string> darkMediaIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(lightMediaIds);
        ArgumentNullException.ThrowIfNull(darkMediaIds);
        return RunDurableCommandAsync(async context =>
        {
            if (imageStackEngine is null)
                throw new PlatformNotSupportedException("当前宿主未注册多帧影像后端。");
            var availableMediaIds = context.Session.Media
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            var lights = lightMediaIds.Where(availableMediaIds.Contains)
                .Distinct(StringComparer.Ordinal).ToArray();
            var darks = darkMediaIds.Where(availableMediaIds.Contains)
                .Except(lights, StringComparer.Ordinal)
                .Distinct(StringComparer.Ordinal).ToArray();
            var minimumLights = settings.Mode == WMStackMode.StarTrail ? 2 : 3;
            if (lights.Length < minimumLights)
                throw new InvalidOperationException(
                    $"{(settings.Mode == WMStackMode.StarTrail ? "星轨" : "静态星空/降噪")}至少需要 {minimumLights} 张 Light 照片。");

            var orderedIds = lights.Concat(darks).ToArray();
            var inputs = orderedIds.Select(mediaId =>
                ResolveArtifact(context.Session,
                    context.Session.Media.First(item => item.Id == mediaId))).ToArray();
            var normalizedSettings = settings with
            {
                DarkArtifactIds = darks.Select(mediaId => ResolveArtifact(
                    context.Session,
                    context.Session.Media.First(item => item.Id == mediaId)).Id).ToArray()
            };
            EnsureMultiFrameCapability(normalizedSettings.Mode, inputs);
            var targetMediaId = lights[0];
            var workingDirectory = Path.Combine(
                ResolveSessionDirectory(inputs[0]), "artifacts", "multi-frame");
            Directory.CreateDirectory(workingDirectory);

            using var operation = jobs.Begin(context.Token);
            try
            {
                var checkpoint = new WMWorkspaceJobCheckpoint(
                    Guid.NewGuid().ToString("N"),
                    WMWorkspaceJobKind.MultiFrame,
                    WMWorkspaceJobStatus.Running,
                    System.Text.Json.JsonSerializer.Serialize(new WMWorkspaceMultiFrameSelection(
                        targetMediaId, lights, darks, normalizedSettings)),
                    [],
                    DateTime.UtcNow,
                    DateTime.UtcNow);
                var checkpointSession = NextRevision(context.Session with
                {
                    ActiveJobCheckpoint = checkpoint,
                    RequiredFeatures = context.Session.RequiredFeatures
                        .Append(normalizedSettings.Mode == WMStackMode.StarTrail
                            ? WMImagingFeature.StarTrail
                            : WMImagingFeature.MultiFrame)
                        .Distinct()
                        .ToArray()
                });
                CommitPassiveSession(context.Epoch, checkpointSession, value => value with
                {
                    ActiveJob = WMWorkspaceProjection.Job(checkpoint),
                    MultiFrameTool = value.MultiFrameTool with { IsBusy = true }
                });
                await PersistAsync(checkpointSession, context.Epoch, context.Token).ConfigureAwait(false);
                TryUpdate(context.Epoch, null, value => value with
                {
                    Activity = WMWorkspaceActivity.Previewing,
                    Stage = WMOperationStage.Queued,
                    Progress = 0,
                    Message = "正在准备多帧合成…",
                    CanCancel = true,
                    ErrorMessage = null,
                    ActiveJob = value.ActiveJob with
                    {
                        Stage = WMOperationStage.Queued,
                        Progress = 0,
                        Message = "正在准备多帧合成…",
                        CanCancel = true
                    }
                });
                var progress = new Progress<WMOperationProgress>(report =>
                    TryUpdate(context.Epoch, null, value => value with
                    {
                        Activity = WMWorkspaceActivity.Previewing,
                        Stage = report.Stage,
                        Progress = report.Percentage,
                        Message = report.Message,
                        CanCancel = report.CanCancel,
                        ActiveJob = value.ActiveJob with
                        {
                            Stage = report.Stage,
                            Progress = report.Percentage,
                            Message = report.Message,
                            CanCancel = report.CanCancel
                        }
                    }));
                var result = await imageStackEngine.ExecuteAsync(
                    new WMOperationRequest(
                        inputs,
                        normalizedSettings,
                        false,
                        workingDirectory,
                        progress,
                        executionProfiles?.GetInteractiveProfile()),
                    normalizedSettings,
                    operation.Token).ConfigureAwait(false);
                var output = result.Outputs.SingleOrDefault()
                             ?? throw new InvalidOperationException("多帧后端没有返回稳定产物。");
                if (!IsCurrent(context.Epoch))
                    throw new OperationCanceledException(operation.Token);

                var artifacts = checkpointSession.Artifacts
                    .Concat(checkpointSession.Media.Select(item => item.Artifact))
                    .Append(output)
                    .GroupBy(item => item.Id, StringComparer.Ordinal)
                    .Select(group => group.Last())
                    .ToArray();
                var currentArtifacts = new Dictionary<string, string>(
                    checkpointSession.CurrentArtifactIdsByMediaId, StringComparer.Ordinal)
                {
                    [targetMediaId] = output.Id
                };
                var selection = new WMWorkspaceMultiFrameSelection(
                    targetMediaId, lights, darks, normalizedSettings);
                var recordedOperation = result.Operation with
                {
                    ParametersJson = System.Text.Json.JsonSerializer.Serialize(selection)
                };
                var cursor = Math.Clamp(
                    checkpointSession.HistoryCursor, 0, checkpointSession.Transactions.Count);
                var transactions = checkpointSession.Transactions.Take(cursor)
                    .Append(new WMWorkspaceTransaction
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Label = normalizedSettings.Mode == WMStackMode.StarTrail ? "星轨合成" : "多帧合成",
                        Assignments = [new WMWorkspaceOperationAssignment([targetMediaId], [recordedOperation])],
                        AddedMediaIds = [],
                        RemovedMediaIds = [],
                        CreatedAtUtc = DateTime.UtcNow
                    })
                    .ToArray();
                var completedCheckpoint = checkpoint with
                {
                    Status = WMWorkspaceJobStatus.Completed,
                    StableArtifactIds = [output.Id],
                    UpdatedAtUtc = DateTime.UtcNow
                };
                var updated = NextRevision(checkpointSession with
                {
                    Mode = WMWorkspaceMode.MultiFrame,
                    Artifacts = artifacts,
                    CurrentArtifactIdsByMediaId = currentArtifacts,
                    Transactions = transactions,
                    HistoryCursor = transactions.Length,
                    Operations = GetBaselineOperations(checkpointSession)
                        .Concat(transactions.SelectMany(item => item.EffectiveOperations))
                        .ToArray(),
                    CurrentMediaId = targetMediaId,
                    ActiveJobCheckpoint = completedCheckpoint
                });
                var intent = CommitSession(
                    context.Epoch, updated, clearColorDraft: false, value => value with
                    {
                        Mode = WMWorkspaceMode.MultiFrame,
                        Media = WMWorkspaceProjection.Media(updated),
                        CurrentMediaId = targetMediaId,
                        Activity = WMWorkspaceActivity.Idle,
                        Message = "多帧合成完成",
                        Progress = 100,
                        CanCancel = false,
                        ActiveJob = WMWorkspaceProjection.Job(completedCheckpoint),
                        MultiFrameTool = value.MultiFrameTool with { IsBusy = false }
                    });
                await PersistAndPreviewAsync(
                    updated, context.Epoch, intent, operation.Token).ConfigureAwait(false);
                await RecordTraceAsync(
                    updated.Id,
                    output.ContentHash,
                    checkpoint.Id,
                    "multi-frame-completed",
                    cacheHit: false,
                    canceled: false,
                    errorCode: null).ConfigureAwait(false);
            }
            finally
            {
                jobs.Complete(operation);
                TryUpdate(context.Epoch, null, value => value with
                {
                    MultiFrameTool = value.MultiFrameTool with { IsBusy = false }
                });
            }
        }, cancellationToken);
    }

    public Task UndoAsync(CancellationToken cancellationToken = default)
    {
        int target;
        lock (gate) target = Math.Max(0, (session?.HistoryCursor ?? 0) - 1);
        return SetHistoryCursorAsync(target, cancellationToken);
    }

    public Task RedoAsync(CancellationToken cancellationToken = default)
    {
        int target;
        lock (gate) target = Math.Min(
            session?.Transactions.Count ?? 0,
            (session?.HistoryCursor ?? 0) + 1);
        return SetHistoryCursorAsync(target, cancellationToken);
    }

    public Task SetHistoryCursorAsync(
        int cursor,
        CancellationToken cancellationToken = default) =>
        RunDurableCommandAsync(async context =>
        {
            var target = Math.Clamp(cursor, 0, context.Session.Transactions.Count);
            if (target == context.Session.HistoryCursor && !State.HasTransientEdits) return;
            draftColorRecipe = null;
            draftTemplateEdit = null;
            var updated = NextRevision(MaterializeAtCursor(context.Session, target));
            var intent = CommitSession(context.Epoch, updated, clearColorDraft: true, value => value with
            {
                Media = WMWorkspaceProjection.Media(updated),
                CurrentMediaId = updated.CurrentMediaId,
                TemplateId = GetEffectiveTemplateId(updated, updated.CurrentMediaId),
                TemplateEdit = GetEffectiveTemplateEdit(updated, updated.CurrentMediaId),
                ColorRecipe = CloneRecipe(GetEffectiveColorRecipe(updated, updated.CurrentMediaId)),
                ColorGradeTool = value.ColorGradeTool with
                {
                    Draft = CloneRecipe(GetEffectiveColorRecipe(updated, updated.CurrentMediaId))
                            ?? new WMColorRecipe { Name = "工作台调整" }
                },
                HasTransientEdits = false,
                TransientEditMode = null,
                IsComparingOriginal = false
            });
            await PersistAndPreviewAsync(updated, context.Epoch, intent, context.Token).ConfigureAwait(false);
        }, cancellationToken);

    public Task SetCompareOriginalAsync(bool comparing, CancellationToken cancellationToken = default)
    {
        var task = SetCompareOriginalCoreAsync(comparing, cancellationToken);
        TrackOwned(task);
        return task;
    }

    private async Task SetCompareOriginalCoreAsync(bool comparing, CancellationToken cancellationToken)
    {
        long currentEpoch;
        WMWorkspaceState currentState;
        CancellationToken epochToken;
        lock (gate)
        {
            if (closed || epochCancellation is null) return;
            currentEpoch = epoch;
            currentState = state;
            epochToken = epochCancellation.Token;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, epochToken);
        if (currentState.IsComparingOriginal == comparing) return;
        if (!comparing)
        {
            WMObjectUrlLease? lease;
            lock (gate)
            {
                lease = originalLease;
                originalLease = null;
            }
            if (lease is not null) await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
            TryUpdate(currentEpoch, null, value => value with
            {
                PreviewUrl = previewLease?.Url,
                IsComparingOriginal = false
            });
            return;
        }

        WMWorkspaceMedia? media;
        lock (gate)
        {
            media = session?.Media.FirstOrDefault(item =>
                string.Equals(item.Id, currentState.CurrentMediaId, StringComparison.Ordinal));
        }
        var previewPath = media?.Artifact.PreviewPath ?? media?.Artifact.FilePath;
        if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath)) return;
        await using var content = new FileStream(
            previewPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var ownerVersion = Interlocked.Increment(ref nextBlobVersion);
        var published = await objectUrls.PublishAsync(
            OriginalOwner, ownerVersion, content, MimeFromPath(previewPath), linked.Token).ConfigureAwait(false);
        if (published is null) return;
        if (!IsCurrent(currentEpoch))
        {
            await objectUrls.ReleaseAsync(published).ConfigureAwait(false);
            return;
        }
        lock (gate) originalLease = published;
        TryUpdate(currentEpoch, null, value => value with
        {
            PreviewUrl = published.Url,
            IsComparingOriginal = true
        });
    }

    private async Task PublishColorReferenceAsync(
        WMColorReferenceImport imported,
        long requiredEpoch,
        CancellationToken cancellationToken)
    {
        WMObjectUrlLease? previous;
        lock (gate)
        {
            previous = colorReferenceLease;
            colorReferenceLease = null;
        }
        if (previous is not null) await objectUrls.ReleaseAsync(previous).ConfigureAwait(false);
        await using var stream = new FileStream(
            imported.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var lease = await objectUrls.PublishAsync(
            "workspace:color-reference",
            Interlocked.Increment(ref nextBlobVersion),
            stream,
            MimeFromPath(imported.FilePath),
            cancellationToken).ConfigureAwait(false);
        if (lease is null) return;
        if (!IsCurrent(requiredEpoch))
        {
            await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
            return;
        }
        lock (gate)
        {
            colorReferenceLease = lease;
            colorReferencePath = imported.FilePath;
            colorReferenceName = imported.DisplayName;
        }
        TryUpdate(requiredEpoch, null, value => value with
        {
            ColorGradeTool = value.ColorGradeTool with
            {
                Reference = new WMColorReferenceState(imported.DisplayName, lease.Url)
            }
        });
    }

    private void RefreshColorPresets(string? selectedPresetId)
    {
        var presets = colorPresetLibrary?.Load() ?? [];
        UpdateCurrent(value => value with
        {
            ColorGradeTool = value.ColorGradeTool with
            {
                Presets = presets,
                SelectedPresetId = selectedPresetId
            }
        });
    }

    public Task UpdateExportDraftAsync(
        WMExportDraft draft,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = draft with
        {
            Quality = Math.Clamp(draft.Quality, 60, 100),
            CustomMaximumLongEdge = Math.Clamp(draft.CustomMaximumLongEdge, 320, 16384),
            MaximumLongEdge = draft.MaximumLongEdge is null or -1 or 1920 or 3840
                ? draft.MaximumLongEdge
                : Math.Clamp(draft.MaximumLongEdge.Value, 320, 16384)
        };
        UpdateCurrent(value => value with
        {
            ExportTool = value.ExportTool with { Draft = normalized }
        });
        return Task.CompletedTask;
    }

    public Task StartExportAsync(
        IReadOnlyList<string>? mediaIds = null,
        CancellationToken cancellationToken = default)
    {
        var stateSnapshot = State;
        var targets = mediaIds is { Count: > 0 }
            ? mediaIds
            : stateSnapshot.Media.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
        var draft = stateSnapshot.ExportTool.Draft;
        var maximumLongEdge = draft.MaximumLongEdge == -1
            ? draft.CustomMaximumLongEdge
            : draft.MaximumLongEdge;
        var request = new WMExportRequest(
            targets,
            draft.Format,
            maximumLongEdge,
            draft.Quality,
            draft.Format == WMExportFormat.Tiff16
                ? WMExportDestinationKind.SystemPicker
                : draft.Destination);
        var task = RunExportJobAsync(request, cancellationToken);
        TrackOwned(task);
        return task;
    }

    public Task RetryFailedExportAsync(CancellationToken cancellationToken = default) =>
        StartExportAsync(State.ExportTool.FailedMediaIds, cancellationToken);

    private async Task RunExportJobAsync(
        WMExportRequest request,
        CancellationToken cancellationToken)
    {
        var checkpoint = new WMWorkspaceJobCheckpoint(
            Guid.NewGuid().ToString("N"),
            WMWorkspaceJobKind.Export,
            WMWorkspaceJobStatus.Running,
            System.Text.Json.JsonSerializer.Serialize(request),
            [],
            DateTime.UtcNow,
            DateTime.UtcNow);
        await RunDurableCommandAsync(async context =>
        {
            var requiredFeatures = context.Session.RequiredFeatures.ToList();
            if (request.Format == WMExportFormat.Png16) requiredFeatures.Add(WMImagingFeature.Png16);
            if (request.Format == WMExportFormat.Tiff16) requiredFeatures.Add(WMImagingFeature.Tiff16);
            var updated = NextRevision(context.Session with
            {
                ActiveJobCheckpoint = checkpoint,
                RequiredFeatures = requiredFeatures.Distinct().ToArray()
            });
            CommitPassiveSession(context.Epoch, updated, value => value with
            {
                ActiveJob = WMWorkspaceProjection.Job(checkpoint),
                ExportTool = value.ExportTool with { IsBusy = true, Results = [] }
            });
            await PersistAsync(updated, context.Epoch, context.Token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await ExportCoreAsync(request, cancellationToken).ConfigureAwait(false);
            await CompleteJobCheckpointAsync(
                checkpoint,
                WMWorkspaceJobStatus.Completed,
                result.Items.Where(item => item.Status == WMExportItemStatus.Succeeded)
                    .Select(item => item.MediaId).ToArray(),
                null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await CompleteJobCheckpointAsync(
                checkpoint,
                WMWorkspaceJobStatus.Canceled,
                [],
                "用户取消导出。",
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await CompleteJobCheckpointAsync(
                checkpoint,
                WMWorkspaceJobStatus.Failed,
                [],
                ex.Message,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private Task CompleteJobCheckpointAsync(
        WMWorkspaceJobCheckpoint checkpoint,
        WMWorkspaceJobStatus status,
        IReadOnlyList<string> stableArtifactIds,
        string? errorMessage,
        CancellationToken cancellationToken) =>
        RunDurableCommandAsync(async context =>
        {
            if (context.Session.ActiveJobCheckpoint?.Id != checkpoint.Id) return;
            var completed = checkpoint with
            {
                Status = status,
                StableArtifactIds = stableArtifactIds,
                UpdatedAtUtc = DateTime.UtcNow,
                ErrorMessage = errorMessage
            };
            var updated = NextRevision(context.Session with { ActiveJobCheckpoint = completed });
            CommitPassiveSession(context.Epoch, updated, value => value with
            {
                ActiveJob = WMWorkspaceProjection.Job(completed),
                ExportTool = value.ExportTool with { IsBusy = false },
                MultiFrameTool = value.MultiFrameTool with { IsBusy = false },
                CollageTool = value.CollageTool with { IsBusy = false }
            });
            await PersistAsync(updated, context.Epoch, context.Token).ConfigureAwait(false);
            await RecordTraceAsync(
                updated.Id,
                null,
                checkpoint.Id,
                $"job-{status.ToString().ToLowerInvariant()}",
                cacheHit: false,
                canceled: status == WMWorkspaceJobStatus.Canceled,
                errorCode: string.IsNullOrWhiteSpace(errorMessage) ? null : status.ToString())
                .ConfigureAwait(false);
        }, cancellationToken);

    public Task<WMExportResult> ExportJpegAsync(
        int quality = 92,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> mediaIds;
        lock (gate)
        {
            mediaIds = session is null
                ? []
                : ResolveTargetMediaIds(session, WMApplyScope.Selected);
        }
        return ExportAsync(new WMExportRequest(
            mediaIds,
            WMExportFormat.Jpeg8,
            null,
            quality,
            WMExportDestinationKind.PlatformDefault), cancellationToken);
    }

    public Task<WMExportResult> ExportAsync(
        WMExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var task = ExportCoreAsync(request, cancellationToken);
        TrackOwned(task);
        return task;
    }

    public void CancelCurrentOperation()
    {
        long currentEpoch;
        lock (gate)
        {
            currentEpoch = epoch;
        }
        jobs.Cancel();
        renderCoordinator.CancelPreview();
        TryUpdate(currentEpoch, null, value => value with
        {
            Activity = WMWorkspaceActivity.Idle,
            Message = "已取消",
            CanCancel = false
        });
    }

    public Task CancelActiveJobAsync()
    {
        CancelCurrentOperation();
        UpdateCurrent(value => value with
        {
            ActiveJob = value.ActiveJob with
            {
                Status = WMWorkspaceJobStatus.Canceled,
                CanCancel = false,
                CanRetry = true,
                Message = "任务已取消"
            },
            MultiFrameTool = value.MultiFrameTool with { IsBusy = false },
            ExportTool = value.ExportTool with { IsBusy = false },
            CollageTool = value.CollageTool with { IsBusy = false }
        });
        return Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        await lifecycleLock.WaitAsync().ConfigureAwait(false);
        try { await CloseEpochAsync().ConfigureAwait(false); }
        finally { lifecycleLock.Release(); }
    }

    public async Task DiscardAsync()
    {
        var id = State.SessionId;
        await CloseAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(id)) await sessionStore.DeleteAsync(id).ConfigureAwait(false);
    }

    private Task CommitColorGradeAsync(
        WMColorRecipe recipe,
        WMApplyScope scope,
        CancellationToken cancellationToken) =>
        RunDurableCommandAsync(async context =>
        {
            var targetIds = ResolveTargetMediaIds(context.Session, scope);
            if (targetIds.Count == 0)
            {
                var legacyUpdated = NextRevision(context.Session with { ColorRecipe = CloneRecipe(recipe) });
                var legacyIntent = CommitSession(context.Epoch, legacyUpdated, clearColorDraft: true, value => value with
                {
                    ColorRecipe = CloneRecipe(recipe),
                    ColorGradeTool = value.ColorGradeTool with
                    {
                        Draft = CloneRecipe(recipe)!,
                        IsBusy = false
                    },
                    HasTransientEdits = false,
                    TransientEditMode = null,
                    IsComparingOriginal = false
                });
                await PersistAndPreviewAsync(
                    legacyUpdated, context.Epoch, legacyIntent, context.Token).ConfigureAwait(false);
                return;
            }
            var overrides = context.Session.ColorRecipesByMediaId.ToDictionary(
                pair => pair.Key,
                pair => CloneRecipe(pair.Value),
                StringComparer.Ordinal);
            foreach (var mediaId in targetIds) overrides[mediaId] = CloneRecipe(recipe);
            var updated = context.Session with
            {
                ColorRecipe = context.Session.ColorRecipe,
                ColorRecipesByMediaId = overrides
            };
            updated = AppendTransaction(
                updated,
                "调整颜色",
                WMImageOperationKind.ColorGrade,
                targetIds,
                recipe);
            updated = NextRevision(updated);
            var currentRecipe = GetEffectiveColorRecipe(updated, updated.CurrentMediaId);
            var intent = CommitSession(context.Epoch, updated, clearColorDraft: true, value => value with
            {
                ColorRecipe = CloneRecipe(currentRecipe),
                ColorGradeTool = value.ColorGradeTool with
                {
                    Draft = CloneRecipe(currentRecipe) ?? new WMColorRecipe { Name = "工作台调整" },
                    IsBusy = false
                },
                HasTransientEdits = false,
                TransientEditMode = null,
                IsComparingOriginal = false
            });
            await PersistAndPreviewAsync(updated, context.Epoch, intent, context.Token).ConfigureAwait(false);
        }, cancellationToken);

    private Task PreviewColorGradeAsync(
        WMColorRecipe recipe,
        WMApplyScope scope,
        CancellationToken cancellationToken)
    {
        WMWorkspaceSession previewSession;
        long currentEpoch;
        long intent;
        lock (gate)
        {
            if (closed || session is null || epochCancellation is null)
                throw new InvalidOperationException("工作台会话尚未打开。");
            currentEpoch = epoch;
            draftColorRecipe = CloneRecipe(recipe);
            var targetIds = ResolveTargetMediaIds(session, scope);
            var overrides = session.ColorRecipesByMediaId.ToDictionary(
                pair => pair.Key,
                pair => CloneRecipe(pair.Value),
                StringComparer.Ordinal);
            foreach (var mediaId in targetIds) overrides[mediaId] = CloneRecipe(recipe);
            previewSession = targetIds.Count == 0
                ? session with { ColorRecipe = CloneRecipe(recipe) }
                : session with { ColorRecipesByMediaId = overrides };
            intent = ++intentRevision;
            state = state with
            {
                ColorRecipe = CloneRecipe(recipe),
                ColorGradeTool = state.ColorGradeTool with
                {
                    Draft = CloneRecipe(recipe)!,
                    IsBusy = false
                },
                HasTransientEdits = true,
                TransientEditMode = WMWorkspaceMode.ColorGrade,
                IsComparingOriginal = false,
                CanUndo = session.HistoryCursor > 0,
                CanRedo = session.HistoryCursor < session.Transactions.Count
            };
        }
        Changed.Invoke(State);
        return QueuePreviewTrackedAsync(previewSession, currentEpoch, intent, cancellationToken);
    }

    private async Task<WMExportResult> ExportCoreAsync(
        WMExportRequest request,
        CancellationToken cancellationToken)
    {
        WMWorkspaceSession current;
        IReadOnlyList<WMWorkspaceMedia> mediaItems;
        long currentEpoch;
        CancellationToken epochToken;
        lock (gate)
        {
            if (closed || session is null || epochCancellation is null)
                throw new InvalidOperationException("工作台会话尚未打开。");
            current = session;
            currentEpoch = epoch;
            epochToken = epochCancellation.Token;
            EnsureExportCapability(request.Format);
            var requestedIds = request.MediaIds.ToHashSet(StringComparer.Ordinal);
            mediaItems = current.Media.Where(item => requestedIds.Contains(item.Id)).ToArray();
            if (mediaItems.Count == 0)
                throw new InvalidOperationException("请至少选择一张要导出的图片。");
        }
        TryUpdate(currentEpoch, null, value => value with
        {
            Activity = WMWorkspaceActivity.Exporting,
            Stage = WMOperationStage.Queued,
            Progress = 0,
            Message = $"正在准备导出 {mediaItems.Count} 张图片…",
            CanCancel = true,
            ErrorMessage = null,
            ExportTool = value.ExportTool with { IsBusy = true, Results = [] },
            ActiveJob = value.ActiveJob with
            {
                Stage = WMOperationStage.Queued,
                Progress = 0,
                Message = $"正在准备导出 {mediaItems.Count} 张图片…",
                CanCancel = true
            }
        });

        using var operation = jobs.Begin(cancellationToken, epochToken);
        try
        {
            var results = new List<WMExportItemResult>(mediaItems.Count);
            for (var index = 0; index < mediaItems.Count; index++)
            {
                operation.Token.ThrowIfCancellationRequested();
                var media = mediaItems[index];
                var renderMedia = media with { Artifact = ResolveArtifact(current, media) };
                TryUpdate(currentEpoch, null, value => value with
                {
                    Stage = WMOperationStage.Processing,
                    Progress = 100d * index / mediaItems.Count,
                    Message = $"正在导出 {index + 1}/{mediaItems.Count}：{media.DisplayName}",
                    ActiveJob = value.ActiveJob with
                    {
                        Stage = WMOperationStage.Processing,
                        Progress = 100d * index / mediaItems.Count,
                        Message = $"正在导出 {index + 1}/{mediaItems.Count}：{media.DisplayName}",
                        CanCancel = true
                    }
                });
                var fileName = Path.ChangeExtension(media.DisplayName, request.Format switch
                {
                    WMExportFormat.Png16 => ".png",
                    WMExportFormat.Tiff16 => ".tiff",
                    _ => ".jpg"
                });
                try
                {
                    var path = await exportService.ExportAsync(
                        current,
                        renderMedia,
                        request.Format,
                        request.Quality,
                        request.MaximumLongEdge,
                        operation.Token).ConfigureAwait(false);
                    var destinationHandle = await exportSink.SaveAsync(
                        path,
                        fileName,
                        request.Format,
                        request.Destination,
                        operation.Token).ConfigureAwait(false);
                    results.Add(new WMExportItemResult(
                        media.Id,
                        WMExportItemStatus.Succeeded,
                        path,
                        fileName,
                        destinationHandle,
                        null));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    results.Add(new WMExportItemResult(
                        media.Id,
                        WMExportItemStatus.Failed,
                        null,
                        fileName,
                        null,
                        ex.Message));
                }
            }
            if (!IsCurrent(currentEpoch)) throw new OperationCanceledException(operation.Token);
            var succeeded = results.Count(item => item.Status == WMExportItemStatus.Succeeded);
            TryUpdate(currentEpoch, null, value => value with
            {
                Activity = WMWorkspaceActivity.Completed,
                Stage = WMOperationStage.Completed,
                Progress = 100,
                Message = succeeded == mediaItems.Count
                    ? $"已导出 {succeeded} 张图片"
                    : $"已导出 {succeeded} 张，失败 {mediaItems.Count - succeeded} 张",
                CanCancel = false,
                ExportTool = value.ExportTool with { IsBusy = false, Results = results.ToArray() },
                ActiveJob = value.ActiveJob with
                {
                    Stage = WMOperationStage.Completed,
                    Progress = 100,
                    Message = "导出完成",
                    CanCancel = false,
                    CanRetry = results.Any(item => item.Status == WMExportItemStatus.Failed)
                }
            });
            return new WMExportResult(results);
        }
        catch (OperationCanceledException)
        {
            TryUpdate(currentEpoch, null, value => value with
            {
                Activity = WMWorkspaceActivity.Idle,
                Message = "已取消导出",
                CanCancel = false,
                ExportTool = value.ExportTool with { IsBusy = false },
                ActiveJob = value.ActiveJob with
                {
                    Status = WMWorkspaceJobStatus.Canceled,
                    CanCancel = false,
                    CanRetry = true,
                    Message = "已取消导出"
                }
            });
            throw;
        }
        catch (Exception ex)
        {
            Fail(currentEpoch, null, ex);
            throw;
        }
        finally
        {
            jobs.Complete(operation);
            TryUpdate(currentEpoch, null, value => value with
            {
                ExportTool = value.ExportTool with { IsBusy = false }
            });
        }
    }

    private async Task RunDurableCommandAsync(
        Func<CommandContext, Task> command,
        CancellationToken cancellationToken,
        [CallerMemberName] string commandName = "unknown-command")
    {
        string? diagnosticSessionId = State.SessionId;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await durableCommands.RunAsync(async () =>
            {
                WMWorkspaceSession current;
                long currentEpoch;
                CancellationToken epochToken;
                lock (gate)
                {
                    if (closed || session is null || epochCancellation is null)
                        throw new InvalidOperationException("工作台会话尚未打开。");
                    current = session;
                    diagnosticSessionId = current.Id;
                    currentEpoch = epoch;
                    epochToken = epochCancellation.Token;
                }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, epochToken);
                await command(new CommandContext(currentEpoch, current, linked.Token)).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Information,
                "workspace-command-completed",
                "工作台命令已完成。",
                diagnosticSessionId,
                properties: new Dictionary<string, string>
                {
                    ["command"] = commandName,
                    ["elapsedMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                    ["revision"] = CurrentRevision().ToString()
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Information,
                "workspace-command-canceled",
                "工作台命令已取消。",
                diagnosticSessionId,
                properties: new Dictionary<string, string>
                {
                    ["command"] = commandName,
                    ["elapsedMs"] = stopwatch.ElapsedMilliseconds.ToString()
                }).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Error,
                "workspace-command-failed",
                ex.Message,
                diagnosticSessionId,
                ex,
                new Dictionary<string, string>
                {
                    ["command"] = commandName,
                    ["elapsedMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                    ["revision"] = CurrentRevision().ToString()
                }).ConfigureAwait(false);
            throw;
        }
    }

    private async Task PersistAndPreviewAsync(
        WMWorkspaceSession current,
        long currentEpoch,
        long intent,
        CancellationToken cancellationToken)
    {
        await PersistAsync(current, currentEpoch, cancellationToken).ConfigureAwait(false);
        await RefreshMediaPreviewUrlsAsync(
            WMWorkspaceProjection.Media(current),
            currentEpoch,
            notify: true,
            cancellationToken).ConfigureAwait(false);
        if (current.Mode != WMWorkspaceMode.TemplateDesign && current.Media.Count > 0)
            await QueuePreviewTrackedAsync(current, currentEpoch, intent, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshMediaPreviewUrlsAsync(
        IReadOnlyList<WMWorkspaceMedia> media,
        long requiredEpoch,
        bool notify,
        CancellationToken cancellationToken)
    {
        var activeIds = media.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        KeyValuePair<string, WMObjectUrlLease>[] obsolete;
        lock (gate)
        {
            if (!IsCurrentLocked(requiredEpoch)) return;
            obsolete = mediaPreviewLeases
                .Where(pair => !activeIds.Contains(pair.Key))
                .ToArray();
            foreach (var pair in obsolete)
            {
                mediaPreviewLeases.Remove(pair.Key);
                mediaPreviewFingerprints.Remove(pair.Key);
            }
        }

        foreach (var pair in obsolete)
            await objectUrls.ReleaseAsync(pair.Value).ConfigureAwait(false);

        var changed = obsolete.Length > 0;
        foreach (var item in media)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = item.Artifact.PreviewPath ?? item.Artifact.FilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            var fingerprint = MediaPreviewFingerprint(item, path);
            lock (gate)
            {
                if (!IsCurrentLocked(requiredEpoch)) return;
                if (mediaPreviewLeases.ContainsKey(item.Id)
                    && mediaPreviewFingerprints.TryGetValue(item.Id, out var current)
                    && string.Equals(current, fingerprint, StringComparison.Ordinal))
                    continue;
            }

            WMObjectUrlLease? published;
            try
            {
                await using var content = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                published = await objectUrls.PublishAsync(
                    MediaPreviewOwnerPrefix + item.Id,
                    Interlocked.Increment(ref nextBlobVersion),
                    content,
                    MimeFromPath(path),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                continue;
            }
            if (published is null) continue;

            var keep = false;
            lock (gate)
            {
                var current = state.Media.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, item.Id, StringComparison.Ordinal));
                if (IsCurrentLocked(requiredEpoch)
                    && current is not null
                    && string.Equals(
                        MediaPreviewFingerprint(current, current.Artifact.PreviewPath ?? current.Artifact.FilePath),
                        fingerprint,
                        StringComparison.Ordinal))
                {
                    mediaPreviewLeases[item.Id] = published;
                    mediaPreviewFingerprints[item.Id] = fingerprint;
                    keep = true;
                    changed = true;
                }
            }
            if (!keep) await objectUrls.ReleaseAsync(published).ConfigureAwait(false);
        }

        if (notify && changed && IsCurrent(requiredEpoch)) Changed.Invoke(State);
    }

    private static string MediaPreviewFingerprint(WMWorkspaceMedia media, string path) =>
        $"{media.Artifact.Id}|{media.Artifact.ContentHash}|{path}";

    private async Task PersistAsync(
        WMWorkspaceSession current,
        long currentEpoch,
        CancellationToken cancellationToken)
    {
        if (!IsCurrent(currentEpoch)) throw new OperationCanceledException(cancellationToken);
        await sessionStore.SaveAsync(current, cancellationToken).ConfigureAwait(false);
        if (!IsCurrent(currentEpoch)) throw new OperationCanceledException(cancellationToken);
    }

    private Task QueuePreviewTrackedAsync(
        WMWorkspaceSession previewSession,
        long currentEpoch,
        long intent,
        CancellationToken cancellationToken)
    {
        var task = QueuePreviewCoreAsync(previewSession, currentEpoch, intent, cancellationToken);
        TrackOwned(task);
        return task;
    }

    private async Task QueuePreviewCoreAsync(
        WMWorkspaceSession previewSession,
        long currentEpoch,
        long intent,
        CancellationToken cancellationToken)
    {
        CancellationToken epochToken;
        lock (gate)
        {
            if (!IsCurrentLocked(currentEpoch, intent) || epochCancellation is null) return;
            epochToken = epochCancellation.Token;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, epochToken);
        var token = linked.Token;
        var media = previewSession.Media.FirstOrDefault(item => item.Id == previewSession.CurrentMediaId)
                    ?? previewSession.Media.FirstOrDefault();
        if (media is null || !IsCurrent(currentEpoch, intent)) return;
        media = media with { Artifact = ResolveArtifact(previewSession, media) };
        var version = Interlocked.Increment(ref nextPreviewVersion);
        var previewStopwatch = System.Diagnostics.Stopwatch.StartNew();
        if (!TryUpdate(currentEpoch, intent, value => value with
            {
                Activity = WMWorkspaceActivity.Previewing,
                PreviewVersion = version,
                Stage = WMOperationStage.Processing,
                Message = string.Empty,
                Progress = 0,
                CanCancel = true,
                ErrorMessage = null
            })) return;

        IDisposable? candidateArtifactLease = null;
        string? fingerprint = null;
        try
        {
            var compiledPlan = await renderPlanCompiler.CompileAsync(
                previewSession,
                media.Id,
                WMRenderTarget.SettledPreview(),
                token).ConfigureAwait(false);
            var hasTemplate = compiledPlan.Steps.Any(step =>
                step.Operation.Kind == WMImageOperationKind.Template);
            var hasColorRecipe = compiledPlan.Steps.Any(step =>
                step.Operation.Kind == WMImageOperationKind.ColorGrade);
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Information,
                "workspace-preview-started",
                "工作台预览开始。",
                previewSession.Id,
                properties: new Dictionary<string, string>
                {
                    ["previewVersion"] = version.ToString(),
                    ["intentRevision"] = intent.ToString(),
                    ["sessionRevision"] = previewSession.Revision.ToString(),
                    ["mode"] = previewSession.Mode.ToString(),
                    ["hasTemplate"] = hasTemplate.ToString(),
                    ["hasColorRecipe"] = hasColorRecipe.ToString(),
                    ["graphFingerprint"] = compiledPlan.GraphFingerprint
                }).ConfigureAwait(false);
            fingerprint = await previewService.CreateFingerprintAsync(
                compiledPlan,
                token).ConfigureAwait(false);
            if (!IsCurrent(currentEpoch, intent, version)) return;

            var ticket = renderCoordinator.QueuePreview(
                new WMWorkspaceRenderRequest(
                    previewSession.Id,
                    currentEpoch,
                    version,
                    fingerprint,
                    token => previewService.RenderAsync(
                        compiledPlan,
                        version,
                        token)),
                token);
            var preview = await renderCoordinator.FlushAsync(ticket, token).ConfigureAwait(false);
            if (!IsCurrent(currentEpoch, intent, version)) return;
            candidateArtifactLease = artifactCache.AcquireLease(
                ResolveSessionDirectory(media.Artifact),
                fingerprint);

            await using var content = new FileStream(
                preview.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var lease = await objectUrls.PublishAsync(
                PreviewOwner, version, content, preview.MimeType, token).ConfigureAwait(false);
            if (lease is null) return;
            if (!IsCurrent(currentEpoch, intent, version))
            {
                await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
                return;
            }

            WMObjectUrlLease? compareLease;
            IDisposable? replacedArtifactLease;
            lock (gate)
            {
                if (!IsCurrentLocked(currentEpoch, intent, version))
                {
                    compareLease = null;
                    replacedArtifactLease = null;
                }
                else
                {
                    previewLease = lease;
                    compareLease = originalLease;
                    originalLease = null;
                    replacedArtifactLease = previewArtifactLease;
                    previewArtifactLease = candidateArtifactLease;
                    candidateArtifactLease = null;
                }
            }
            replacedArtifactLease?.Dispose();
            if (!IsCurrent(currentEpoch, intent, version))
            {
                await objectUrls.ReleaseAsync(lease).ConfigureAwait(false);
                return;
            }
            if (compareLease is not null) await objectUrls.ReleaseAsync(compareLease).ConfigureAwait(false);
            var published = TryUpdate(currentEpoch, intent, value => value with
            {
                Activity = WMWorkspaceActivity.PreviewReady,
                PreviewUrl = lease.Url,
                PreviewPresentation = value.HasTransientEdits
                                      && value.Mode == WMWorkspaceMode.ColorGrade
                                      && value.PreviewPresentation.ColorProgram is not null
                    ? value.PreviewPresentation with
                    {
                        StableUrl = lease.Url,
                        IsSettled = false
                    }
                    : new WMWorkspacePreviewPresentation(
                        version,
                        fingerprint,
                        lease.Url,
                        lease.Url,
                        null,
                        WMInteractivePreviewBackend.CpuSkia,
                        IsSettled: true,
                        FallbackReason: value.PreviewPresentation.FallbackReason),
                Stage = WMOperationStage.Completed,
                Message = string.Empty,
                Progress = 100,
                CanCancel = false,
                IsComparingOriginal = false
            });
            if (published)
            {
                await RecordTraceAsync(
                    previewSession.Id,
                    fingerprint,
                    null,
                    "preview-published",
                    cacheHit: preview.CacheHit,
                    canceled: false,
                    errorCode: null).ConfigureAwait(false);
                previewStopwatch.Stop();
                await RecordLogSafeAsync(
                    WMDiagnosticLogLevel.Information,
                    "workspace-preview-published",
                    "工作台预览已发布。",
                    previewSession.Id,
                    properties: new Dictionary<string, string>
                    {
                        ["previewVersion"] = version.ToString(),
                        ["intentRevision"] = intent.ToString(),
                        ["sessionRevision"] = previewSession.Revision.ToString(),
                        ["elapsedMs"] = previewStopwatch.ElapsedMilliseconds.ToString(),
                        ["cacheHit"] = preview.CacheHit.ToString()
                    }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!IsCurrent(currentEpoch, intent, version))
        {
            await RecordTraceAsync(
                previewSession.Id, fingerprint, null, "preview-superseded", false, true, null)
                .ConfigureAwait(false);
            previewStopwatch.Stop();
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Information,
                "workspace-preview-superseded",
                "工作台预览已被较新版本替代。",
                previewSession.Id,
                properties: new Dictionary<string, string>
                {
                    ["previewVersion"] = version.ToString(),
                    ["intentRevision"] = intent.ToString(),
                    ["elapsedMs"] = previewStopwatch.ElapsedMilliseconds.ToString()
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryUpdate(currentEpoch, intent, value => value with
            {
                Activity = WMWorkspaceActivity.Idle,
                Message = string.Empty,
                CanCancel = false
            });
            await RecordTraceAsync(
                previewSession.Id, fingerprint, null, "preview-canceled", false, true, null)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            previewStopwatch.Stop();
            Fail(currentEpoch, intent, ex);
            await RecordTraceAsync(
                previewSession.Id,
                fingerprint,
                null,
                "preview-failed",
                false,
                false,
                ex.GetType().Name).ConfigureAwait(false);
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Error,
                "workspace-preview-failed",
                ex.Message,
                previewSession.Id,
                ex,
                new Dictionary<string, string>
                {
                    ["previewVersion"] = version.ToString(),
                    ["intentRevision"] = intent.ToString(),
                    ["sessionRevision"] = previewSession.Revision.ToString(),
                    ["elapsedMs"] = previewStopwatch.ElapsedMilliseconds.ToString()
                }).ConfigureAwait(false);
        }
        finally
        {
            candidateArtifactLease?.Dispose();
        }
    }

    private void EnsureExportCapability(WMExportFormat format)
    {
        if (format == WMExportFormat.Jpeg8) return;
        var feature = format switch
        {
            WMExportFormat.Png16 => WMImagingFeature.Png16,
            WMExportFormat.Tiff16 => WMImagingFeature.Tiff16,
            _ => throw new InvalidOperationException($"不支持导出格式 {format}。")
        };
        var capability = imagingCapabilities?.Probe(feature);
        if (capability?.IsAvailable == true) return;
        throw new PlatformNotSupportedException(
            capability?.UnavailableReason ?? "当前宿主尚未开放所选高精度导出格式。");
    }

    private void EnsureMultiFrameCapability(
        WMStackMode mode,
        IReadOnlyList<WMImageArtifact> inputs)
    {
        if (imagingCapabilities is null) return;
        var feature = mode == WMStackMode.StarTrail
            ? WMImagingFeature.StarTrail
            : WMImagingFeature.MultiFrame;
        var requiredDisk = WMImagingDiskEstimator.EstimateRequiredBytes(inputs);
        var capability = imagingCapabilities.Probe(feature, requiredDisk);
        if (capability.IsAvailable) return;
        throw new PlatformNotSupportedException(
            capability.UnavailableReason ?? "当前设备尚未开放所选多帧能力。");
    }

    private long CommitSession(
        long currentEpoch,
        WMWorkspaceSession updated,
        bool clearColorDraft,
        Func<WMWorkspaceState, WMWorkspaceState> update)
    {
        WMWorkspaceState next;
        long intent;
        lock (gate)
        {
            if (!IsCurrentLocked(currentEpoch)) throw new OperationCanceledException();
            session = updated;
            if (clearColorDraft) draftColorRecipe = null;
            intent = ++intentRevision;
            state = next = NormalizeRuntimeTools(update(state), updated.Media) with
            {
                CanUndo = updated.HistoryCursor > 0,
                CanRedo = updated.HistoryCursor < updated.Transactions.Count,
                History = WMWorkspaceProjection.History(updated),
                HistoryCursor = updated.HistoryCursor
            };
        }
        Changed.Invoke(next);
        return intent;
    }

    private void CommitPassiveSession(
        long currentEpoch,
        WMWorkspaceSession updated,
        Func<WMWorkspaceState, WMWorkspaceState> update)
    {
        WMWorkspaceState next;
        lock (gate)
        {
            if (!IsCurrentLocked(currentEpoch)) throw new OperationCanceledException();
            session = updated;
            state = next = NormalizeRuntimeTools(update(state), updated.Media) with
            {
                CanUndo = updated.HistoryCursor > 0,
                CanRedo = updated.HistoryCursor < updated.Transactions.Count,
                History = WMWorkspaceProjection.History(updated),
                HistoryCursor = updated.HistoryCursor
            };
        }
        Changed.Invoke(next);
    }

    private async Task CloseEpochAsync()
    {
        CancellationTokenSource? cancellation;
        WMObjectUrlLease? preview;
        WMObjectUrlLease? original;
        WMObjectUrlLease? colorReference;
        WMObjectUrlLease? colorBase;
        WMObjectUrlLease[] mediaPreviews;
        IDisposable? artifactLease;
        IDisposable? colorBaseArtifact;
        IDisposable? lease;
        string? closingSessionId;
        lock (gate)
        {
            if (closed
                && epochCancellation is null
                && sessionLease is null
                && previewArtifactLease is null
                && mediaPreviewLeases.Count == 0) return;
            closed = true;
            epoch++;
            cancellation = epochCancellation;
            epochCancellation = null;
            preview = previewLease;
            original = originalLease;
            colorReference = colorReferenceLease;
            colorBase = colorBaseLease;
            mediaPreviews = mediaPreviewLeases.Values.ToArray();
            previewLease = null;
            originalLease = null;
            colorReferenceLease = null;
            colorBaseLease = null;
            mediaPreviewLeases.Clear();
            mediaPreviewFingerprints.Clear();
            artifactLease = previewArtifactLease;
            colorBaseArtifact = colorBaseArtifactLease;
            previewArtifactLease = null;
            colorBaseArtifactLease = null;
            colorBaseFingerprint = null;
            colorBaseTask = null;
            gpuColorPreviewAvailable = null;
            lease = sessionLease;
            sessionLease = null;
            closingSessionId = session?.Id ?? state.SessionId;
            draftColorRecipe = null;
            draftTemplateEdit = null;
            colorReferencePath = null;
            colorReferenceName = null;
        }
        jobs.Cancel();
        cancellation?.Cancel();
        transientPreviews.Cancel();
        renderCoordinator.CancelPreview();

        await durableCommands.DrainAsync().ConfigureAwait(false);
        await AwaitOwnedTasksAsync().ConfigureAwait(false);

        if (preview is not null) await objectUrls.ReleaseAsync(preview).ConfigureAwait(false);
        if (original is not null) await objectUrls.ReleaseAsync(original).ConfigureAwait(false);
        if (colorReference is not null) await objectUrls.ReleaseAsync(colorReference).ConfigureAwait(false);
        if (colorBase is not null) await objectUrls.ReleaseAsync(colorBase).ConfigureAwait(false);
        foreach (var mediaPreview in mediaPreviews)
            await objectUrls.ReleaseAsync(mediaPreview).ConfigureAwait(false);
        artifactLease?.Dispose();
        colorBaseArtifact?.Dispose();
        cancellation?.Dispose();
        lease?.Dispose();
        if (!string.IsNullOrWhiteSpace(closingSessionId))
        {
            await RecordLogSafeAsync(
                WMDiagnosticLogLevel.Information,
                "workspace-closed",
                "工作台会话已关闭，后台任务与对象地址已释放。",
                closingSessionId).ConfigureAwait(false);
        }
    }

    private async Task AwaitOwnedTasksAsync()
    {
        while (true)
        {
            Task[] snapshot;
            lock (gate) snapshot = ownedTasks.ToArray();
            if (snapshot.Length == 0) return;
            try { await Task.WhenAll(snapshot).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { }
        }
    }

    private void TrackOwned(Task task)
    {
        lock (gate) ownedTasks.Add(task);
        _ = task.ContinueWith(
            completed =>
            {
                lock (gate) ownedTasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private bool TryUpdate(
        long requiredEpoch,
        long? requiredIntent,
        Func<WMWorkspaceState, WMWorkspaceState> update)
    {
        WMWorkspaceState next;
        lock (gate)
        {
            if (!IsCurrentLocked(requiredEpoch, requiredIntent)) return false;
            var projected = update(state);
            state = next = NormalizeRuntimeTools(projected, projected.Media) with
            {
                CanUndo = session is { HistoryCursor: > 0 },
                CanRedo = session is not null && session.HistoryCursor < session.Transactions.Count,
                History = session is null ? [] : WMWorkspaceProjection.History(session),
                HistoryCursor = session?.HistoryCursor ?? 0
            };
        }
        Changed.Invoke(next);
        return true;
    }

    private static WMWorkspaceState NormalizeRuntimeTools(
        WMWorkspaceState value,
        IReadOnlyList<WMWorkspaceMedia> media)
    {
        var available = media.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var roles = value.MultiFrameTool.Draft.Roles
            .Where(pair => available.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var item in media) roles.TryAdd(item.Id, WMFrameRole.Light);
        var collageOrder = value.CollageTool.Draft.OrderedMediaIds
            .Where(available.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return value with
        {
            MultiFrameTool = value.MultiFrameTool with
            {
                Draft = value.MultiFrameTool.Draft with { Roles = roles }
            },
            CollageTool = value.CollageTool with
            {
                Draft = value.CollageTool.Draft with { OrderedMediaIds = collageOrder }
            }
        };
    }

    private static WMMultiFrameDraft NormalizeMultiFrameDraft(
        WMMultiFrameDraft? draft,
        IReadOnlyList<WMWorkspaceMedia> media)
    {
        var mode = draft?.Mode ?? WMStackMode.StarTrail;
        var defaults = WMMultiFrameStackSettings.CreateDefault(mode);
        var available = media.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var roles = (draft?.Roles ?? new Dictionary<string, WMFrameRole?>())
            .Where(pair => available.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var item in media) roles.TryAdd(item.Id, WMFrameRole.Light);
        return new WMMultiFrameDraft(
            mode,
            roles,
            draft?.NormalizeExposure ?? defaults.NormalizeExposure,
            draft?.RepairHotPixels ?? defaults.RepairHotPixels,
            draft?.AutoCrop ?? defaults.AutoCrop);
    }

    private static WMCollageDraft NormalizeCollageDraft(
        WMCollageDraft? draft,
        IReadOnlyList<WMWorkspaceMedia> media)
    {
        var available = media.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var ordered = (draft?.OrderedMediaIds ?? media.Select(item => item.Id).ToArray())
            .Where(available.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new WMCollageDraft(ordered, draft?.Direction ?? WMCollageDirection.Horizontal);
    }

    private static WMMultiFrameDraft CopyMultiFrameDraft(WMMultiFrameDraft draft) =>
        draft with
        {
            Roles = new Dictionary<string, WMFrameRole?>(draft.Roles, StringComparer.Ordinal)
        };

    private static WMCollageDraft CopyCollageDraft(WMCollageDraft draft) =>
        draft with { OrderedMediaIds = draft.OrderedMediaIds.ToArray() };

    private void UpdateCurrent(Func<WMWorkspaceState, WMWorkspaceState> update)
    {
        long currentEpoch;
        lock (gate) currentEpoch = epoch;
        TryUpdate(currentEpoch, null, update);
    }

    private void ApplySessionToStateLocked()
    {
        var currentMediaId = session!.CurrentMediaId;
        state = state with
        {
            Mode = session.Mode,
            ReturnPath = session.ReturnPath,
            Media = WMWorkspaceProjection.Media(session),
            TemplateId = GetEffectiveTemplateId(session, currentMediaId),
            TemplateEdit = GetEffectiveTemplateEdit(session, currentMediaId),
            ColorRecipe = CloneRecipe(GetEffectiveColorRecipe(session, currentMediaId)),
            ColorGradeTool = state.ColorGradeTool with
            {
                Draft = CloneRecipe(GetEffectiveColorRecipe(session, currentMediaId))
                        ?? new WMColorRecipe { Name = "工作台调整" }
            },
            MultiFrameTool = state.MultiFrameTool with
            {
                Draft = NormalizeMultiFrameDraft(session.MultiFrameConfiguration, session.Media)
            },
            CollageTool = state.CollageTool with
            {
                Draft = NormalizeCollageDraft(session.CollageConfiguration, session.Media)
            },
            CurrentMediaId = currentMediaId,
            CanUndo = session.HistoryCursor > 0,
            CanRedo = session.HistoryCursor < session.Transactions.Count,
            History = WMWorkspaceProjection.History(session),
            HistoryCursor = session.HistoryCursor,
            HasTransientEdits = false,
            TransientEditMode = null,
            IsComparingOriginal = false
        };
    }

    private void Fail(long requiredEpoch, long? requiredIntent, Exception exception) =>
        TryUpdate(requiredEpoch, requiredIntent, value => value with
        {
            Activity = WMWorkspaceActivity.Failed,
            Message = "处理失败",
            ErrorMessage = exception.Message,
            CanCancel = false
        });

    private bool IsCurrent(long requiredEpoch)
    {
        lock (gate) return IsCurrentLocked(requiredEpoch);
    }

    private bool IsCurrent(long requiredEpoch, long requiredIntent)
    {
        lock (gate) return IsCurrentLocked(requiredEpoch, requiredIntent);
    }

    private bool IsCurrent(long requiredEpoch, long requiredIntent, long requiredVersion)
    {
        lock (gate) return IsCurrentLocked(requiredEpoch, requiredIntent, requiredVersion);
    }

    private bool IsCurrentLocked(
        long requiredEpoch,
        long? requiredIntent = null,
        long? requiredVersion = null) =>
        !closed
        && epoch == requiredEpoch
        && (!requiredIntent.HasValue || intentRevision == requiredIntent.Value)
        && (!requiredVersion.HasValue || state.PreviewVersion == requiredVersion.Value);

    private static WMWorkspaceSession NextRevision(WMWorkspaceSession value) =>
        value with { Revision = checked(value.Revision + 1) };

    private static WMColorRecipe NormalizeRecipe(WMColorRecipe recipe)
    {
        // Grade is authoritative in the workspace. Deserialized v5 recipes can
        // contain a stale compatibility copy in UserAdjustments.
        var normalized = CloneRecipe(recipe)!;
        normalized.UserAdjustments = normalized.Grade;
        normalized.Normalize();
        return normalized;
    }

    private static WMColorRecipe? CloneRecipe(WMColorRecipe? recipe) => recipe is null
        ? null
        : WMColorRecipeSnapshot.Copy(recipe)!;

    private static string? GetEffectiveTemplateId(WMWorkspaceSession value, string? mediaId)
    {
        if (!string.IsNullOrWhiteSpace(mediaId)
            && value.TemplateIdsByMediaId.TryGetValue(mediaId, out var templateId))
            return templateId;
        return value.TemplateId;
    }

    private static WMWorkspaceTemplateEdit? GetEffectiveTemplateEdit(
        WMWorkspaceSession value,
        string? mediaId)
    {
        var templateId = GetEffectiveTemplateId(value, mediaId);
        if (string.IsNullOrWhiteSpace(templateId) || string.IsNullOrWhiteSpace(mediaId))
            return templateId is null ? null : new WMWorkspaceTemplateEdit(templateId, null);
        var media = value.Media.FirstOrDefault(item => item.Id == mediaId)
                    ?? Catalog(value).FirstOrDefault(item => item.Id == mediaId);
        return new WMWorkspaceTemplateEdit(
            templateId,
            media is null ? null : GetEffectiveTemplateSnapshotJson(value, media));
    }

    private static WMColorRecipe? GetEffectiveColorRecipe(WMWorkspaceSession value, string? mediaId)
    {
        if (!string.IsNullOrWhiteSpace(mediaId)
            && value.ColorRecipesByMediaId.TryGetValue(mediaId, out var recipe))
            return recipe;
        return value.ColorRecipe;
    }

    private static IReadOnlyList<string> ResolveTargetMediaIds(
        WMWorkspaceSession value,
        WMApplyScope scope)
    {
        if (scope == WMApplyScope.Current)
            return string.IsNullOrWhiteSpace(value.CurrentMediaId) ? [] : [value.CurrentMediaId];
        var available = value.Media.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var selected = value.SelectedMediaIds.Where(available.Contains).Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length > 0) return selected;
        selected = value.Media.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
        if (selected.Length > 0) return selected;
        return string.IsNullOrWhiteSpace(value.CurrentMediaId) ? [] : [value.CurrentMediaId];
    }

    private static WMWorkspaceSession AppendTransaction<TSettings>(
        WMWorkspaceSession value,
        string label,
        WMImageOperationKind kind,
        IReadOnlyList<string> mediaIds,
        TSettings settings)
    {
        var targetIds = mediaIds.ToHashSet(StringComparer.Ordinal);
        var inputArtifactIds = value.Media
            .Where(item => targetIds.Contains(item.Id))
            .Select(item => ResolveArtifact(value, item).Id)
            .ToArray();
        var operation = WMImageOperation.Create(
            kind,
            inputArtifactIds,
            inputArtifactIds.Select(_ => Guid.NewGuid().ToString("N")),
            settings);
        var cursor = Math.Clamp(value.HistoryCursor, 0, value.Transactions.Count);
        var transactions = value.Transactions.Take(cursor).Append(new WMWorkspaceTransaction
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = label,
            Assignments = [new WMWorkspaceOperationAssignment(mediaIds, [operation])],
            AddedMediaIds = [],
            RemovedMediaIds = [],
            CreatedAtUtc = DateTime.UtcNow
        }).ToArray();
        var baselineOperations = GetBaselineOperations(value);
        return value with
        {
            Transactions = transactions,
            HistoryCursor = transactions.Length,
            Operations = baselineOperations.Concat(transactions.SelectMany(item => item.EffectiveOperations)).ToArray()
        };
    }

    private static WMWorkspaceSession AppendStructuralTransaction(
        WMWorkspaceSession value,
        string label,
        IReadOnlyList<WMWorkspaceOperationAssignment> assignments,
        IReadOnlyList<string> addedMediaIds,
        IReadOnlyList<string> removedMediaIds)
    {
        var cursor = Math.Clamp(value.HistoryCursor, 0, value.Transactions.Count);
        var transactions = value.Transactions.Take(cursor).Append(new WMWorkspaceTransaction
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = label,
            Assignments = assignments,
            AddedMediaIds = addedMediaIds,
            RemovedMediaIds = removedMediaIds,
            CreatedAtUtc = DateTime.UtcNow
        }).ToArray();
        return value with
        {
            Transactions = transactions,
            HistoryCursor = transactions.Length,
            Operations = GetBaselineOperations(value)
                .Concat(transactions.SelectMany(item => item.EffectiveOperations))
                .ToArray()
        };
    }

    private static WMWorkspaceSession MaterializeAtCursor(WMWorkspaceSession value, int requestedCursor)
    {
        var cursor = Math.Clamp(requestedCursor, 0, value.Transactions.Count);
        var catalog = Catalog(value);
        var mediaByArtifact = catalog.ToDictionary(
            item => item.Artifact.Id,
            item => item.Id,
            StringComparer.Ordinal);
        var currentArtifacts = catalog.ToDictionary(
            item => item.Id,
            item => item.Artifact.Id,
            StringComparer.Ordinal);
        var addedByHistory = value.Transactions
            .SelectMany(item => item.AddedMediaIds)
            .ToHashSet(StringComparer.Ordinal);
        var activeMediaIds = catalog.Select(item => item.Id)
            .Where(id => !addedByHistory.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var transaction in value.Transactions.Take(cursor))
        {
            activeMediaIds.UnionWith(transaction.AddedMediaIds);
            activeMediaIds.ExceptWith(transaction.RemovedMediaIds);
        }
        var templateOverrides = new Dictionary<string, string?>(StringComparer.Ordinal);
        var colorOverrides = new Dictionary<string, WMColorRecipe?>(StringComparer.Ordinal);
        var operationTargets = value.Transactions.Take(cursor)
            .SelectMany(transaction => transaction.Assignments)
            .SelectMany(assignment => assignment.Operations.Select(operation =>
                new KeyValuePair<string, IReadOnlyList<string>>(operation.Id, assignment.MediaIds)))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.SelectMany(pair => pair.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var operations = GetBaselineOperations(value)
            .Concat(value.Transactions.Take(cursor).SelectMany(item => item.EffectiveOperations))
            .ToArray();
        foreach (var operation in operations)
        {
            var targetMediaIds = operationTargets.TryGetValue(operation.Id, out var assigned)
                ? assigned
                : operation.InputArtifactIds
                    .Where(mediaByArtifact.ContainsKey)
                    .Select(artifactId => mediaByArtifact[artifactId])
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            if (operation.Kind == WMImageOperationKind.Template)
            {
                WMWorkspaceTemplateSelection? selection;
                try
                {
                    selection = System.Text.Json.JsonSerializer
                        .Deserialize<WMWorkspaceTemplateSelection>(operation.ParametersJson);
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }
                foreach (var mediaId in targetMediaIds)
                    templateOverrides[mediaId] = selection?.TemplateId;
            }
            else if (operation.Kind == WMImageOperationKind.ColorGrade)
            {
                WMColorRecipe? recipe;
                try
                {
                    recipe = System.Text.Json.JsonSerializer.Deserialize<WMColorRecipe>(operation.ParametersJson);
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }
                foreach (var mediaId in targetMediaIds)
                    colorOverrides[mediaId] = CloneRecipe(recipe);
            }
            else if (operation.Kind == WMImageOperationKind.MultiFrameStack)
            {
                WMWorkspaceMultiFrameSelection? selection;
                try
                {
                    selection = System.Text.Json.JsonSerializer
                        .Deserialize<WMWorkspaceMultiFrameSelection>(operation.ParametersJson);
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }
                var outputId = operation.OutputArtifactIds.FirstOrDefault();
                if (selection is null
                    || string.IsNullOrWhiteSpace(outputId)
                    || !value.Artifacts.Any(item => item.Id == outputId)) continue;
                currentArtifacts[selection.TargetMediaId] = outputId;
                mediaByArtifact[outputId] = selection.TargetMediaId;
            }
        }
        var activeSet = activeMediaIds.ToHashSet(StringComparer.Ordinal);
        var activeMedia = catalog.Where(item => activeSet.Contains(item.Id)).ToArray();
        var selected = value.SelectedMediaIds.Where(activeSet.Contains).Distinct(StringComparer.Ordinal).ToArray();
        var currentMediaId = activeSet.Contains(value.CurrentMediaId ?? string.Empty)
            ? value.CurrentMediaId
            : activeMedia.FirstOrDefault()?.Id;
        return value with
        {
            HistoryCursor = cursor,
            Operations = operations,
            MediaCatalog = catalog,
            ActiveMediaIds = catalog.Where(item => activeSet.Contains(item.Id)).Select(item => item.Id).ToArray(),
            Media = activeMedia,
            SelectedMediaIds = selected,
            CurrentMediaId = currentMediaId,
            TemplateIdsByMediaId = templateOverrides,
            ColorRecipesByMediaId = colorOverrides,
            CurrentArtifactIdsByMediaId = currentArtifacts
        };
    }

    private static IReadOnlyList<WMImageOperation> GetBaselineOperations(WMWorkspaceSession value)
    {
        if (value.Transactions.Count == 0) return value.Operations;
        var transactionOperationIds = value.Transactions
            .SelectMany(item => item.EffectiveOperations)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        return value.Operations.Where(item => !transactionOperationIds.Contains(item.Id)).ToArray();
    }

    private static string? GetEffectiveTemplateSnapshotJson(
        WMWorkspaceSession value,
        WMWorkspaceMedia media)
    {
        var relatedArtifactIds = GetRelatedArtifactIds(value, media);
        foreach (var operation in value.Operations.Reverse())
        {
            if (operation.Kind != WMImageOperationKind.Template
                || !operation.InputArtifactIds.Any(relatedArtifactIds.Contains)) continue;
            try
            {
                return System.Text.Json.JsonSerializer
                    .Deserialize<WMWorkspaceTemplateSelection>(operation.ParametersJson)?.CanvasJson;
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
        return null;
    }

    private static HashSet<string> GetRelatedArtifactIds(
        WMWorkspaceSession value,
        WMWorkspaceMedia media)
    {
        var artifactsById = value.Artifacts
            .Concat(Catalog(value).Select(item => item.Artifact))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var related = new HashSet<string>(StringComparer.Ordinal) { media.Artifact.Id };
        var pending = new Stack<string>();
        pending.Push(ResolveArtifact(value, media).Id);
        while (pending.Count > 0)
        {
            var artifactId = pending.Pop();
            if (!related.Add(artifactId) || !artifactsById.TryGetValue(artifactId, out var artifact)) continue;
            foreach (var parentId in artifact.ParentArtifactIds) pending.Push(parentId);
        }
        return related;
    }

    private static string ResolveSessionDirectory(WMImageArtifact artifact)
    {
        var path = artifact.PreviewPath ?? artifact.FilePath;
        var containingDirectory = Directory.GetParent(path)
                                  ?? throw new InvalidOperationException("素材路径没有有效目录。");
        return containingDirectory.Parent?.FullName ?? containingDirectory.FullName;
    }

    private static string MimeFromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    private static WMImageArtifact ResolveArtifact(
        WMWorkspaceSession value,
        WMWorkspaceMedia media)
    {
        if (value.CurrentArtifactIdsByMediaId.TryGetValue(media.Id, out var artifactId))
        {
            var artifact = value.Artifacts.FirstOrDefault(item =>
                string.Equals(item.Id, artifactId, StringComparison.Ordinal));
            if (artifact is not null) return artifact;
        }
        return media.Artifact;
    }

    private Task RecordTraceAsync(
        string sessionId,
        string? fingerprint,
        string? jobId,
        string eventName,
        bool cacheHit,
        bool canceled,
        string? errorCode)
    {
        if (traceStore is null) return Task.CompletedTask;
        var metrics = performanceCounters?.Snapshot()
                      ?? new WMWorkspaceMetricSnapshot(
                          new Dictionary<WMWorkspaceMetricStage, int>(),
                          new Dictionary<WMWorkspaceMetricStage, double>());
        return traceStore.RecordAsync(new WMWorkspaceTraceEvent(
            DateTime.UtcNow,
            WMWorkspaceTraceStore.SessionKey(sessionId),
            fingerprint,
            jobId,
            eventName,
            metrics.Calls,
            metrics.DurationMilliseconds,
            Environment.WorkingSet,
            cacheHit,
            canceled,
            errorCode));
    }

    private async Task RecordLogSafeAsync(
        WMDiagnosticLogLevel level,
        string eventName,
        string? message,
        string? sessionId,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (traceStore is null) return;
        try
        {
            var write = traceStore.RecordLogAsync(new WMDiagnosticLogEvent(
                DateTime.UtcNow,
                level,
                "Workspace.Controller",
                eventName,
                message,
                exception?.GetType().FullName,
                exception is null ? null : $"0x{exception.HResult:X8}",
                string.IsNullOrWhiteSpace(sessionId)
                    ? null
                    : WMWorkspaceTraceStore.SessionKey(sessionId),
                properties,
                exception?.StackTrace));
            if (level >= WMDiagnosticLogLevel.Error)
                await write.ConfigureAwait(false);
            else
                WMDiagnosticLoggerProvider.Observe(write);
        }
        catch
        {
            // Diagnostics must never change editing, recovery, or export behavior.
        }
    }

    private long CurrentRevision()
    {
        lock (gate) return session?.Revision ?? 0;
    }

    private static IReadOnlyList<WMWorkspaceMedia> Catalog(WMWorkspaceSession value) =>
        value.MediaCatalog.Count > 0 ? value.MediaCatalog : value.Media;

    private sealed record CommandContext(
        long Epoch,
        WMWorkspaceSession Session,
        CancellationToken Token);

    private sealed record ColorBasePreview(string Fingerprint, string Url);
}
