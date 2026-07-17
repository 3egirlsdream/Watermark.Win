#nullable enable

using System.Text.Json.Serialization;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public enum WMWorkspaceMode
{
    Template,
    ColorGrade,
    MultiFrame,
    Collage,
    TemplateDesign
}

public enum WMWorkspaceActivity
{
    Idle,
    Loading,
    Previewing,
    PreviewReady,
    Exporting,
    Completed,
    Failed
}

public enum WMWorkspacePanelSize
{
    Collapsed,
    Half,
    Expanded
}

public enum WMMobileEditorSpace
{
    Small,
    Medium,
    Large
}

public enum WMMobileEditorHostKind
{
    Dock,
    StageOverlay
}

public enum WMMobileEditorTool
{
    TemplatePicker,
    TemplateBorderTop,
    TemplateBorderRight,
    TemplateBorderBottom,
    TemplateBorderLeft,
    TemplateScope,
    ColorStyle,
    ColorExposure,
    ColorContrast,
    ColorHighlights,
    ColorShadows,
    ColorWhites,
    ColorBlacks,
    ColorTemperature,
    ColorTint,
    ColorVibrance,
    ColorSaturation,
    ColorHslHue,
    ColorHslSaturation,
    ColorHslLuminance,
    ColorPresets,
    ColorCurve,
    ColorReference,
    ColorScope,
    MultiFrameMaterial,
    MultiFrameMode,
    MultiFrameParameters,
    MultiFrameGenerate,
    CollageMaterial,
    CollageLayout,
    CollageGenerate
}

public sealed record WMMobileToolPresentation(
    WMWorkspaceMode Category,
    WMMobileEditorTool Tool,
    WMMobileEditorSpace Space,
    WMMobileEditorHostKind HostKind = WMMobileEditorHostKind.Dock);

public enum WMApplyScope
{
    Current,
    Selected
}

public enum WMExportDestinationKind
{
    PlatformDefault,
    SystemPicker
}

public enum WMExportItemStatus
{
    Succeeded,
    Failed,
    Canceled
}

public enum WMTemplateMarketplaceStatus
{
    Succeeded,
    Cancelled,
    LoginRequired,
    Denied,
    Failed
}

public sealed record WMWorkspaceCreateRequest(
    WMWorkspaceMode Mode,
    IReadOnlyList<string> SourcePaths,
    string? TemplateId = null,
    string? ReturnPath = null);

public interface IWMPhotoImportSource : IAsyncDisposable
{
    string DisplayName { get; }
    string? MimeType { get; }
    ValueTask<Stream> OpenReadAsync(CancellationToken token);
}

public sealed class WMPhotoImportSource : IWMPhotoImportSource
{
    private readonly Func<CancellationToken, Task<Stream>> openReadAsync;
    private readonly Func<ValueTask>? disposeAsync;

    public WMPhotoImportSource(
        string displayName,
        Func<CancellationToken, Task<Stream>> openReadAsync,
        string? mimeType = null,
        Func<ValueTask>? disposeAsync = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("导入素材必须具有显示名称。", nameof(displayName));
        DisplayName = displayName;
        this.openReadAsync = openReadAsync ?? throw new ArgumentNullException(nameof(openReadAsync));
        MimeType = mimeType;
        this.disposeAsync = disposeAsync;
    }

    public string DisplayName { get; }
    public string? MimeType { get; }

    public ValueTask<Stream> OpenReadAsync(CancellationToken token) =>
        new(openReadAsync(token));

    public ValueTask DisposeAsync() => disposeAsync?.Invoke() ?? ValueTask.CompletedTask;
}

public sealed record WMWorkspaceMedia
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string OriginalReference { get; init; }
    public required WMImageArtifact Artifact { get; init; }
    public bool IsSelected { get; init; } = true;
}

public sealed record WMWorkspaceSession
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string Id { get; init; }
    public long Revision { get; init; }
    public WMWorkspaceMode Mode { get; init; } = WMWorkspaceMode.Template;
    /// <summary>
    /// Safe in-app route to restore when the workspace is opened from the
    /// recent-session list instead of an in-memory navigation stack.
    /// </summary>
    public string? ReturnPath { get; init; }
    public string? TemplateId { get; init; }
    public WMColorRecipe? ColorRecipe { get; init; }
    public IReadOnlyDictionary<string, string?> TemplateIdsByMediaId { get; init; } =
        new Dictionary<string, string?>();
    public IReadOnlyDictionary<string, WMColorRecipe?> ColorRecipesByMediaId { get; init; } =
        new Dictionary<string, WMColorRecipe?>();
    /// <summary>
    /// Compatibility projection written for v1/v2 recovery. New code treats
    /// MediaCatalog and ActiveMediaIds as authoritative.
    /// </summary>
    public IReadOnlyList<WMWorkspaceMedia> Media { get; init; } = [];
    public IReadOnlyList<WMWorkspaceMedia> MediaCatalog { get; init; } = [];
    public IReadOnlyList<string> ActiveMediaIds { get; init; } = [];
    public IReadOnlyList<WMImageArtifact> Artifacts { get; init; } = [];
    public IReadOnlyDictionary<string, string> CurrentArtifactIdsByMediaId { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyList<string> SelectedMediaIds { get; init; } = [];
    public string? CurrentMediaId { get; init; }
    public IReadOnlyList<WMImageOperation> Operations { get; init; } = [];
    public IReadOnlyList<WMWorkspaceTransaction> Transactions { get; init; } = [];
    public int HistoryCursor { get; init; }
    public WMWorkspaceTemplateDraft? TemplateDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WMMultiFrameDraft? MultiFrameConfiguration { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WMCollageDraft? CollageConfiguration { get; init; }
    public IReadOnlyList<WMImagingFeature> RequiredFeatures { get; init; } = [];
    public WMWorkspaceJobCheckpoint? ActiveJobCheckpoint { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; init; } = DateTime.UtcNow.AddHours(24);
}

public sealed record WMWorkspaceTemplateDraft(
    string TemplateId,
    string OriginalCanvasJson,
    WMTemplateEditorDraftState EditorState,
    DateTime UpdatedAtUtc);

public sealed record WMWorkspaceOperationAssignment(
    IReadOnlyList<string> MediaIds,
    IReadOnlyList<WMImageOperation> Operations);

public sealed record WMWorkspaceTransaction
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public IReadOnlyList<WMWorkspaceOperationAssignment> Assignments { get; init; } = [];

    // Schema v1/v2 compatibility. Schema v3 writers leave this empty and use
    // Assignments so an operation's media scope is never inferred from files.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<WMImageOperation> Operations { get; init; } = [];
    public IReadOnlyList<string> AddedMediaIds { get; init; } = [];
    public IReadOnlyList<string> RemovedMediaIds { get; init; } = [];
    public DateTime CreatedAtUtc { get; init; }

    [JsonIgnore]
    public IReadOnlyList<WMImageOperation> EffectiveOperations => Assignments.Count > 0
        ? Assignments.SelectMany(item => item.Operations).ToArray()
        : Operations;
}

public sealed record WMWorkspaceHistoryItem(
    string TransactionId,
    string Label,
    int Cursor,
    bool IsApplied,
    DateTime CreatedAtUtc);

public sealed record WMWorkspaceTemplateSelection(
    string? TemplateId,
    string? CanvasJson);

public sealed record WMWorkspaceTemplateEdit(
    string? TemplateId,
    string? CanvasJson);

public enum WMDerivedMediaKind
{
    Collage
}

public enum WMCollageDirection
{
    Horizontal,
    Vertical
}

public sealed record WMCollageSettings(
    IReadOnlyList<string> SourceMediaIds,
    WMCollageDirection Direction,
    int GapPixels = 0,
    string BackgroundColor = "#FFFFFF");

public sealed record WMCollageDraft(
    IReadOnlyList<string> OrderedMediaIds,
    WMCollageDirection Direction)
{
    public static WMCollageDraft Empty { get; } = new([], WMCollageDirection.Horizontal);
}

public sealed record WMDerivedMediaRequest(
    WMDerivedMediaKind Kind,
    IReadOnlyList<string> SourceMediaIds,
    string Label,
    WMCollageSettings Collage,
    string? SuggestedFileName = null,
    bool SelectResult = true);

public sealed record WMDerivedMediaOutput(
    WMImageArtifact Artifact,
    WMImageOperation Operation,
    string SuggestedFileName);

public interface IWMDerivedMediaProcessor
{
    Task<WMDerivedMediaOutput> ExecuteAsync(
        WMDerivedMediaRequest request,
        IReadOnlyList<WMImageArtifact> inputs,
        string sessionDirectory,
        CancellationToken cancellationToken = default);
}

public sealed record WMWorkspaceMultiFrameSelection(
    string TargetMediaId,
    IReadOnlyList<string> LightMediaIds,
    IReadOnlyList<string> DarkMediaIds,
    WMMultiFrameStackSettings Settings);

public sealed record WMFrameRoleChange(string MediaId, WMFrameRole? Role);

public sealed record WMMultiFrameDraft(
    WMStackMode Mode,
    IReadOnlyDictionary<string, WMFrameRole?> Roles,
    bool NormalizeExposure,
    bool RepairHotPixels,
    bool AutoCrop);

public sealed record WMExportRequest(
    IReadOnlyList<string> MediaIds,
    WMExportFormat Format,
    int? MaximumLongEdge,
    int Quality,
    WMExportDestinationKind Destination);

public sealed record WMExportItemResult(
    string MediaId,
    WMExportItemStatus Status,
    string? RenderedPath,
    string SuggestedFileName,
    string? DestinationHandle,
    string? ErrorMessage);

public sealed record WMExportResult(IReadOnlyList<WMExportItemResult> Items)
{
    public bool IsSuccess => Items.Count > 0 && Items.All(item => item.Status == WMExportItemStatus.Succeeded);
}

public sealed record WMExportDraft(
    WMExportFormat Format,
    int? MaximumLongEdge,
    int CustomMaximumLongEdge,
    int Quality,
    WMExportDestinationKind Destination)
{
    public static WMExportDraft Default { get; } = new(
        WMExportFormat.Jpeg8, null, 4096, 92, WMExportDestinationKind.PlatformDefault);
}

public enum WMWorkspaceJobKind
{
    MultiFrame,
    Export,
    DerivedMedia
}

public enum WMWorkspaceJobStatus
{
    Preparing,
    Running,
    Interrupted,
    Completed,
    Failed,
    Canceled
}

public sealed record WMWorkspaceJobCheckpoint(
    string Id,
    WMWorkspaceJobKind Kind,
    WMWorkspaceJobStatus Status,
    string RequestJson,
    IReadOnlyList<string> StableArtifactIds,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? ErrorMessage = null);

public sealed record WMWorkspaceJobState(
    string? Id,
    WMWorkspaceJobKind? Kind,
    WMWorkspaceJobStatus? Status,
    WMOperationStage Stage,
    double Progress,
    string Message,
    bool CanCancel,
    bool CanRetry,
    string? ErrorMessage = null)
{
    public static WMWorkspaceJobState Idle { get; } = new(
        null, null, null, WMOperationStage.Queued, 0, string.Empty, false, false);
}

public sealed record WMColorReferenceState(
    string? DisplayName,
    string? PreviewUrl);

public sealed record WMColorGradeToolState(
    WMColorRecipe Draft,
    IReadOnlyList<WMColorRecipe> Presets,
    string? SelectedPresetId,
    WMColorReferenceState Reference,
    bool IsBusy);

public sealed record WMMultiFrameToolState(
    WMMultiFrameDraft Draft,
    WMImagingCapabilityStatus? Capability,
    bool IsBusy);

public sealed record WMCollageToolState(
    WMCollageDraft Draft,
    bool IsBusy);

public sealed record WMExportToolState(
    WMExportDraft Draft,
    IReadOnlyList<WMExportItemResult> Results,
    bool IsBusy)
{
    public IReadOnlyList<string> FailedMediaIds => Results
        .Where(item => item.Status == WMExportItemStatus.Failed)
        .Select(item => item.MediaId)
        .ToArray();
}

public sealed record WMTemplateDesignToolState(
    string? SelectedControlId,
    bool IsDirty,
    bool IsGestureActive,
    int HistoryCursor,
    int HistoryCount);

public enum WMWorkspaceOpenStatus
{
    Opened,
    Missing,
    CorruptManifest,
    MissingMedia,
    MissingTemplateResource,
    UnsupportedVersion
}

public enum WMWorkspaceRecoveryAction
{
    RestoreBackup,
    RemoveAffectedMedia,
    RemoveTemplateOperation,
    DiscardSession
}

public sealed record WMWorkspaceRecoveryIssue(
    WMWorkspaceOpenStatus Status,
    string Message,
    IReadOnlyList<string> AffectedIds,
    IReadOnlyList<WMWorkspaceRecoveryAction> AvailableActions);

public sealed record WMWorkspaceOpenResult(
    WMWorkspaceOpenStatus Status,
    WMWorkspaceSession? Session,
    IReadOnlyList<WMWorkspaceRecoveryIssue> Issues)
{
    public bool IsOpened => Status == WMWorkspaceOpenStatus.Opened && Session is not null;

    public static WMWorkspaceOpenResult Opened(WMWorkspaceSession session) =>
        new(WMWorkspaceOpenStatus.Opened, session, []);
}

public sealed record WMWorkspaceRecoveryRequest(
    WMWorkspaceRecoveryAction Action,
    IReadOnlyList<string> AffectedIds);

public sealed record WMTemplateMarketplaceResult(
    WMTemplateMarketplaceStatus Status,
    string? Message = null)
{
    public bool IsSuccess => Status == WMTemplateMarketplaceStatus.Succeeded;
}

public sealed record WMTemplateMarketplacePageResult(
    WMTemplateMarketplaceStatus Status,
    IReadOnlyList<WMZipedTemplate> Items,
    string? Message = null,
    int NextStart = 0,
    bool HasMore = false,
    int SourceRequestCount = 0)
{
    public bool IsSuccess => Status == WMTemplateMarketplaceStatus.Succeeded;
}

public enum WMTemplateMarketCategory
{
    Recommended,
    Popular,
    Latest,
    Collage
}

public sealed record WMTemplateMarketplaceQuery(
    WMTemplateMarketCategory Category,
    string Keyword,
    int Start,
    int PageSize = 20);

public sealed record WMLocalTemplateResult(
    WMTemplateMarketplaceStatus Status,
    string? TemplateId = null,
    string? UndoToken = null,
    string? Message = null)
{
    public bool IsSuccess => Status == WMTemplateMarketplaceStatus.Succeeded;
}

public sealed record WMColorReferenceImport(
    string DisplayName,
    string FilePath,
    WMColorReferenceProfile Profile);

public sealed record WMWorkspacePreview(
    long Version,
    string Fingerprint,
    string FilePath,
    string MimeType,
    int Width,
    int Height,
    bool CacheHit = false);

public sealed record WMWorkspaceRenderRequest(
    string SessionId,
    long Epoch,
    long Version,
    string Fingerprint,
    Func<CancellationToken, Task<WMWorkspacePreview>> RenderAsync);

public sealed record WMWorkspacePreviewTicket(
    string SessionId,
    long Epoch,
    long Version,
    string Fingerprint,
    Task<WMWorkspacePreview> Completion);

public sealed record WMArtifactCacheEntry(
    string Fingerprint,
    string FilePath,
    long Length,
    DateTime LastAccessUtc);

public sealed record WMObjectUrlLease(
    string OwnerKey,
    long OwnerVersion,
    string Url,
    long Generation);

public sealed record WMWorkspaceState
{
    public string? SessionId { get; init; }
    public WMWorkspaceMode Mode { get; init; } = WMWorkspaceMode.Template;
    public string? ReturnPath { get; init; }
    public WMWorkspaceActivity Activity { get; init; } = WMWorkspaceActivity.Idle;
    public WMWorkspacePanelSize PanelSize { get; init; } = WMWorkspacePanelSize.Half;
    public IReadOnlyList<WMWorkspaceMedia> Media { get; init; } = [];
    public string? CurrentMediaId { get; init; }
    public string? TemplateId { get; init; }
    public WMWorkspaceTemplateEdit? TemplateEdit { get; init; }
    public WMColorRecipe? ColorRecipe { get; init; }
    public string? PreviewUrl { get; init; }
    public long PreviewVersion { get; init; }
    public WMWorkspacePreviewPresentation PreviewPresentation { get; init; } =
        WMWorkspacePreviewPresentation.Empty;
    public WMOperationStage Stage { get; init; } = WMOperationStage.Queued;
    public double Progress { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public bool CanUndo { get; init; }
    public bool CanRedo { get; init; }
    public bool CanCancel { get; init; }
    public bool IsComparingOriginal { get; init; }
    public IReadOnlyList<WMWorkspaceHistoryItem> History { get; init; } = [];
    public int HistoryCursor { get; init; }
    public bool HasTransientEdits { get; init; }
    public WMWorkspaceMode? TransientEditMode { get; init; }
    public WMApplyScope ApplyScope { get; init; } = WMApplyScope.Selected;
    public WMColorGradeToolState ColorGradeTool { get; init; } = new(
        new WMColorRecipe { Name = "工作台调整" }, [], null,
        new WMColorReferenceState(null, null), false);
    public WMMultiFrameToolState MultiFrameTool { get; init; } = new(
        new WMMultiFrameDraft(
            WMStackMode.StarTrail,
            new Dictionary<string, WMFrameRole?>(),
            true,
            true,
            true),
        null,
        false);
    public WMCollageToolState CollageTool { get; init; } = new(
        WMCollageDraft.Empty,
        false);
    public WMExportToolState ExportTool { get; init; } = new(
        WMExportDraft.Default, [], false);
    public WMTemplateDesignToolState? TemplateDesign { get; init; }
    public WMWorkspaceOpenResult? Recovery { get; init; }
    public WMWorkspaceJobState ActiveJob { get; init; } = WMWorkspaceJobState.Idle;

    public WMWorkspaceMedia? CurrentMedia =>
        Media.FirstOrDefault(item => string.Equals(item.Id, CurrentMediaId, StringComparison.Ordinal));
}

public interface IWMWorkspaceSessionStore
{
    IDisposable AcquireLease(string sessionId);
    string GetSessionDirectory(string sessionId);
    Task<string> CreateAsync(WMWorkspaceCreateRequest request, CancellationToken token = default);
    Task<string> CreateAsync(
        WMWorkspaceMode mode,
        IReadOnlyList<IWMPhotoImportSource> sources,
        string? templateId = null,
        CancellationToken token = default);
    Task<WMWorkspaceOpenResult> OpenAsync(string sessionId, CancellationToken token = default);
    Task<WMWorkspaceOpenResult> RecoverAsync(
        string sessionId,
        WMWorkspaceRecoveryAction action,
        IReadOnlyList<string> affectedIds,
        CancellationToken token = default);
    Task SaveAsync(WMWorkspaceSession session, CancellationToken token = default);
    Task DeleteAsync(string sessionId);
    Task<IReadOnlyList<WMWorkspaceSession>> ListRecentAsync(int take = 5, CancellationToken token = default);
    Task CleanupExpiredAsync(CancellationToken token = default);
}

public interface IWMWorkspaceLauncher
{
    Task<string> CreateFromSourcesAsync(
        WMWorkspaceMode mode,
        IReadOnlyList<IWMPhotoImportSource> sources,
        string? templateId,
        CancellationToken token);
}

public interface IWMPhotoPicker
{
    Task<IReadOnlyList<IWMPhotoImportSource>> PickMultipleAsync(
        CancellationToken cancellationToken = default);
}

public interface IWMColorReferenceService
{
    Task<WMColorReferenceImport> ImportAsync(
        string sessionId,
        IWMPhotoImportSource source,
        CancellationToken cancellationToken = default);
}

public interface IWMColorPresetLibrary
{
    IReadOnlyList<WMColorRecipe> Load();
    Task SaveAsync(
        WMColorRecipe recipe,
        string? referenceImagePath,
        CancellationToken cancellationToken = default);
    bool Delete(string presetId);
    string? GetReferenceThumbnailPath(WMColorRecipe recipe);
}

public interface IWMWorkspaceRenderCoordinator
{
    event Action<WMWorkspacePreview>? PreviewPublished;
    WMWorkspacePreviewTicket QueuePreview(WMWorkspaceRenderRequest request, CancellationToken token = default);
    Task<WMWorkspacePreview> FlushAsync(WMWorkspacePreviewTicket ticket, CancellationToken token = default);
    void CancelPreview();
}

public interface IWMArtifactCache
{
    Task<WMArtifactCacheEntry?> TryGetAsync(
        string sessionDirectory,
        string fingerprint,
        CancellationToken cancellationToken = default);

    Task<WMArtifactCacheEntry> CommitAsync(
        string sessionDirectory,
        string fingerprint,
        string filePath,
        long budgetBytes,
        CancellationToken cancellationToken = default);

    IDisposable AcquireLease(string sessionDirectory, string fingerprint);

    Task TrimAsync(
        string sessionDirectory,
        long budgetBytes,
        CancellationToken cancellationToken = default);
}

public interface IWMObjectUrlRegistry : IAsyncDisposable
{
    int ActiveLeaseCount { get; }
    ValueTask<WMObjectUrlLease?> PublishAsync(
        string ownerKey,
        long ownerVersion,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default);
    ValueTask ReleaseAsync(WMObjectUrlLease lease);
}

public interface IWMExecutionProfileProvider
{
    WMOperationExecutionOptions GetInteractiveProfile();
    WMImagingCapabilities GetImagingCapabilities();
}

public interface IWMSystemBackDispatcher
{
    IDisposable Register(Func<bool> handler);
    bool TryDispatch();
}

public interface IWMHapticFeedback
{
    void Perform();
}

public interface IWMSystemAppearance
{
    void SetWorkspaceActive(bool active);
}

public interface IWMTemplateMarketplaceService
{
    Task<WMTemplateMarketplacePageResult> SearchAsync(
        WMTemplateMarketplaceQuery query,
        CancellationToken cancellationToken = default);
    Task<WMTemplateMarketplacePageResult> GetFavoritesAsync(
        CancellationToken cancellationToken = default);
    Task<WMTemplateMarketplaceResult> SetFavoriteAsync(
        WMZipedTemplate template,
        bool favorite,
        CancellationToken cancellationToken = default);
    Task<WMTemplateMarketplaceResult> DownloadAsync(
        WMZipedTemplate template,
        CancellationToken cancellationToken = default);
    Task<WMLocalTemplateResult> CreateLocalAsync(
        IWMPhotoImportSource source,
        string name,
        CancellationToken cancellationToken = default);
    Task<WMLocalTemplateResult> DeleteLocalAsync(
        WMTemplateList template,
        CancellationToken cancellationToken = default);
    Task<WMLocalTemplateResult> UndoDeleteLocalAsync(
        string undoToken,
        CancellationToken cancellationToken = default);
}

public interface IWMExportSink
{
    Task<string> SaveAsync(
        string renderedPath,
        string suggestedFileName,
        WMExportFormat format,
        WMExportDestinationKind destination,
        CancellationToken cancellationToken = default);
}

public interface IWMWorkspaceFeatureFlags
{
    bool IsHeavyImagingEnabled { get; }
    bool IsImagingFeatureEnabled(WMImagingFeature feature);
}

public enum WMImagingFeature
{
    Raw,
    StarTrail,
    MultiFrame,
    Png16,
    Tiff16
}

public sealed record WMImagingCapabilityStatus(
    WMImagingFeature Feature,
    bool BackendAvailable,
    bool FeatureEnabled,
    bool IsAvailable,
    long AvailableMemoryBytes,
    long AvailableDiskBytes,
    string? UnavailableReason);

public interface IWMImagingCapabilityProvider : IWMImagingCapabilities
{
    WMImagingCapabilityStatus Probe(
        WMImagingFeature feature,
        long requiredDiskBytes = 0);
}

public sealed record WMImagingRolloutOptions(
    bool MasterEnabled,
    bool RawEnabled,
    bool StarTrailEnabled,
    bool MultiFrameEnabled,
    bool Png16Enabled,
    bool Tiff16Enabled,
    bool AllowLocalQaOverride)
{
    public bool IsEnabled(WMImagingFeature feature) => MasterEnabled && feature switch
    {
        WMImagingFeature.Raw => RawEnabled,
        WMImagingFeature.StarTrail => StarTrailEnabled,
        WMImagingFeature.MultiFrame => MultiFrameEnabled,
        WMImagingFeature.Png16 => Png16Enabled,
        WMImagingFeature.Tiff16 => Tiff16Enabled,
        _ => false
    };
}

public sealed record WMImagingDiagnosticSnapshot(
    string Platform,
    string Architecture,
    int NativeAbiVersion,
    bool NativeBackendLoaded,
    string BackendVersion,
    uint CapabilityBits,
    long AvailableMemoryBytes,
    long AvailableDiskBytes,
    IReadOnlyList<WMImagingCapabilityStatus> Features,
    DateTime CapturedAtUtc,
    string? ErrorMessage = null);

public interface IWMImagingDiagnosticsService
{
    Task<WMImagingDiagnosticSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public enum WMWorkspaceMetricStage
{
    Decode,
    Scale,
    Replay,
    Encode,
    BlobCreate
}

public sealed record WMWorkspaceMetricSnapshot(
    IReadOnlyDictionary<WMWorkspaceMetricStage, int> Calls,
    IReadOnlyDictionary<WMWorkspaceMetricStage, double> DurationMilliseconds);

public interface IWMWorkspacePerformanceCounters
{
    IDisposable Measure(WMWorkspaceMetricStage stage);
    void Increment(WMWorkspaceMetricStage stage);
    WMWorkspaceMetricSnapshot Snapshot();
    void Reset();
}

public sealed record WMWorkspaceTraceEvent(
    DateTime TimestampUtc,
    string SessionKey,
    string? Fingerprint,
    string? JobId,
    string EventName,
    IReadOnlyDictionary<WMWorkspaceMetricStage, int> Calls,
    IReadOnlyDictionary<WMWorkspaceMetricStage, double> DurationMilliseconds,
    long WorkingMemoryBytes,
    bool CacheHit,
    bool Canceled,
    string? ErrorCode = null);

public enum WMDiagnosticLogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public sealed record WMDiagnosticLogEvent(
    DateTime TimestampUtc,
    WMDiagnosticLogLevel Level,
    string Category,
    string EventName,
    string? Message = null,
    string? ExceptionType = null,
    string? ErrorCode = null,
    string? SessionKey = null,
    IReadOnlyDictionary<string, string>? Properties = null,
    string? StackTrace = null);

public sealed record WMDiagnosticExportResult(
    bool Succeeded,
    string? Location,
    string? ErrorMessage = null);

public interface IWMWorkspaceTraceStore
{
    Task RecordAsync(WMWorkspaceTraceEvent traceEvent, CancellationToken cancellationToken = default);
    Task RecordLogAsync(WMDiagnosticLogEvent logEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WMWorkspaceTraceEvent>> ReadLatestAsync(
        int take = 100,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WMDiagnosticLogEvent>> ReadLatestLogsAsync(
        int take = 200,
        CancellationToken cancellationToken = default);
    Task<string> CreateReportAsync(
        WMImagingDiagnosticSnapshot? imaging = null,
        CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IWMDiagnosticReportExporter
{
    Task<WMDiagnosticExportResult> ExportAsync(
        string reportPath,
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}
