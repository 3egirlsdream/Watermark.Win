#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed class WMWorkspaceSessionStore : IWMWorkspaceSessionStore
{
    private const string ManifestFileName = "manifest.json";
    private const string LastGoodManifestFileName = "manifest.lastgood.bak";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ManifestLocks =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> ActiveSessionLeases =
        new(StringComparer.Ordinal);

    private readonly WMImageImportService importer;
    private readonly IWMExecutionProfileProvider executionProfiles;
    private readonly string root;

    public WMWorkspaceSessionStore(
        WMImageImportService importer,
        IWMExecutionProfileProvider executionProfiles)
        : this(importer, executionProfiles, Path.Combine(Global.AppPath.BasePath, "Cache", "editing-sessions"))
    {
    }

    public WMWorkspaceSessionStore(
        WMImageImportService importer,
        IWMExecutionProfileProvider executionProfiles,
        string root)
    {
        this.importer = importer;
        this.executionProfiles = executionProfiles;
        this.root = root;
        Directory.CreateDirectory(root);
    }

    public IDisposable AcquireLease(string sessionId)
    {
        ValidateSessionId(sessionId);
        var path = ManifestPath(sessionId);
        ActiveSessionLeases.AddOrUpdate(path, 1, (_, count) => checked(count + 1));
        return new SessionLease(path);
    }

    public string GetSessionDirectory(string sessionId)
    {
        ValidateSessionId(sessionId);
        return SessionDirectory(sessionId);
    }

    public async Task<string> CreateEmptyAsync(
        WMWorkspaceMode mode = WMWorkspaceMode.Template,
        string? returnPath = null,
        CancellationToken token = default)
    {
        if (mode == WMWorkspaceMode.TemplateDesign)
            throw new ArgumentException("模板设计会话必须通过模板设计入口创建。", nameof(mode));

        await CleanupExpiredAsync(token).ConfigureAwait(false);
        var id = Guid.NewGuid().ToString("N");
        var directory = SessionDirectory(id);
        Directory.CreateDirectory(directory);
        try
        {
            var now = DateTime.UtcNow;
            var session = new WMWorkspaceSession
            {
                Id = id,
                Mode = mode,
                ReturnPath = string.IsNullOrWhiteSpace(returnPath)
                    ? null
                    : WMReturnUrl.Normalize(returnPath, "/mac"),
                Media = [],
                MediaCatalog = [],
                ActiveMediaIds = [],
                Artifacts = [],
                CurrentArtifactIdsByMediaId = new Dictionary<string, string>(StringComparer.Ordinal),
                SelectedMediaIds = [],
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(24)
            };
            await SaveAsync(session, token).ConfigureAwait(false);
            return id;
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    public async Task<string> CreateAsync(WMWorkspaceCreateRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode != WMWorkspaceMode.TemplateDesign && request.SourcePaths.Count == 0)
            throw new ArgumentException("工作台至少需要一张图片。", nameof(request));
        foreach (var source in request.SourcePaths)
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                throw new FileNotFoundException("导入的图片不存在。", source);

        await CleanupExpiredAsync(token).ConfigureAwait(false);
        var id = Guid.NewGuid().ToString("N");
        var directory = SessionDirectory(id);
        Directory.CreateDirectory(directory);
        try
        {
            var media = await importer.ImportAsync(
                request.SourcePaths,
                directory,
                executionProfiles.GetInteractiveProfile(),
                cancellationToken: token).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var session = new WMWorkspaceSession
            {
                Id = id,
                Mode = request.Mode,
                ReturnPath = string.IsNullOrWhiteSpace(request.ReturnPath)
                    ? null
                    : WMReturnUrl.Normalize(request.ReturnPath, "/create"),
                TemplateId = request.TemplateId,
                Media = media,
                MediaCatalog = media,
                ActiveMediaIds = media.Select(item => item.Id).ToArray(),
                Artifacts = media.Select(item => item.Artifact).ToArray(),
                CurrentArtifactIdsByMediaId = media.ToDictionary(
                    item => item.Id, item => item.Artifact.Id, StringComparer.Ordinal),
                SelectedMediaIds = media.Select(item => item.Id).ToArray(),
                CurrentMediaId = media.FirstOrDefault()?.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(24)
            };
            await SaveAsync(session, token).ConfigureAwait(false);
            return id;
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    public async Task<string> CreateAsync(
        WMWorkspaceMode mode,
        IReadOnlyList<IWMPhotoImportSource> sources,
        string? templateId = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (mode != WMWorkspaceMode.TemplateDesign && sources.Count == 0)
            throw new ArgumentException("工作台至少需要一张图片。", nameof(sources));
        await CleanupExpiredAsync(token).ConfigureAwait(false);
        var id = Guid.NewGuid().ToString("N");
        var directory = SessionDirectory(id);
        Directory.CreateDirectory(directory);
        try
        {
            var media = await importer.ImportAsync(
                sources,
                directory,
                executionProfiles.GetInteractiveProfile(),
                cancellationToken: token).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var session = new WMWorkspaceSession
            {
                Id = id,
                Mode = mode,
                TemplateId = templateId,
                Media = media,
                MediaCatalog = media,
                ActiveMediaIds = media.Select(item => item.Id).ToArray(),
                Artifacts = media.Select(item => item.Artifact).ToArray(),
                CurrentArtifactIdsByMediaId = media.ToDictionary(
                    item => item.Id, item => item.Artifact.Id, StringComparer.Ordinal),
                SelectedMediaIds = media.Select(item => item.Id).ToArray(),
                CurrentMediaId = media.FirstOrDefault()?.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(24)
            };
            await SaveAsync(session, token).ConfigureAwait(false);
            return id;
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    public async Task<WMWorkspaceOpenResult> OpenAsync(
        string sessionId,
        CancellationToken token = default)
    {
        ValidateSessionId(sessionId);
        var path = ManifestPath(sessionId);
        if (!File.Exists(path))
            return new WMWorkspaceOpenResult(WMWorkspaceOpenStatus.Missing, null, []);

        WMWorkspaceSession? session;
        var restoredBackup = false;
        try
        {
            session = await ReadManifestAsync(path, token).ConfigureAwait(false);
            if (session is null)
            {
                (session, restoredBackup) = await TryRestoreBackupAsync(path, token).ConfigureAwait(false);
                if (session is null)
                    return new WMWorkspaceOpenResult(
                        WMWorkspaceOpenStatus.CorruptManifest,
                        null,
                        [new WMWorkspaceRecoveryIssue(
                            WMWorkspaceOpenStatus.CorruptManifest,
                            "编辑会话清单已损坏，且没有可用备份。",
                            [],
                            [WMWorkspaceRecoveryAction.DiscardSession])]);
            }
            if (session.SchemaVersion > WMWorkspaceSession.CurrentSchemaVersion)
                return new WMWorkspaceOpenResult(
                    WMWorkspaceOpenStatus.UnsupportedVersion,
                    null,
                    [new WMWorkspaceRecoveryIssue(
                        WMWorkspaceOpenStatus.UnsupportedVersion,
                        $"该会话由更高版本创建（v{session.SchemaVersion}）。",
                        [],
                        [])]);
            if (session.SchemaVersion < WMWorkspaceSession.CurrentSchemaVersion)
                session = await MigrateLegacyAsync(session, path, token).ConfigureAwait(false);
            session = NormalizeSessionGraph(session);

            if (session.ActiveJobCheckpoint?.Status is WMWorkspaceJobStatus.Preparing or WMWorkspaceJobStatus.Running)
            {
                session = session with
                {
                    ActiveJobCheckpoint = session.ActiveJobCheckpoint with
                    {
                        Status = WMWorkspaceJobStatus.Interrupted,
                        UpdatedAtUtc = DateTime.UtcNow,
                        ErrorMessage = "任务因应用退出而中断，可从最后一个稳定产物重新执行。"
                    }
                };
            }

            var referencedArtifacts = session.MediaCatalog.Select(item => item.Artifact)
                .Concat(session.Artifacts)
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First());
            var missingArtifactIds = referencedArtifacts
                .Where(item => !File.Exists(item.FilePath)
                               || item.PreviewPath is { Length: > 0 } preview && !File.Exists(preview)
                               || item.HighPrecision is { FilePath.Length: > 0 } highPrecision
                               && !File.Exists(highPrecision.FilePath))
                .Select(item => item.Id)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (missingArtifactIds.Length > 0)
                return new WMWorkspaceOpenResult(
                    WMWorkspaceOpenStatus.MissingMedia,
                    session,
                    [new WMWorkspaceRecoveryIssue(
                        WMWorkspaceOpenStatus.MissingMedia,
                        "会话中的部分素材或稳定产物已丢失。",
                        missingArtifactIds,
                        [WMWorkspaceRecoveryAction.RemoveAffectedMedia,
                         WMWorkspaceRecoveryAction.DiscardSession])]);

            var missingTemplateResources = FindMissingTemplateResources(session);
            if (missingTemplateResources.Count > 0)
                return new WMWorkspaceOpenResult(
                    WMWorkspaceOpenStatus.MissingTemplateResource,
                    session,
                    [new WMWorkspaceRecoveryIssue(
                        WMWorkspaceOpenStatus.MissingTemplateResource,
                        "已应用模板的资源快照不完整。",
                        missingTemplateResources,
                        [WMWorkspaceRecoveryAction.RemoveTemplateOperation,
                         WMWorkspaceRecoveryAction.DiscardSession])]);

            var issues = restoredBackup
                ? new[]
                {
                    new WMWorkspaceRecoveryIssue(
                        WMWorkspaceOpenStatus.CorruptManifest,
                        "已从最后一个有效备份恢复会话。",
                        [],
                        [WMWorkspaceRecoveryAction.RestoreBackup])
                }
                : [];
            return new WMWorkspaceOpenResult(WMWorkspaceOpenStatus.Opened, session, issues);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new WMWorkspaceOpenResult(
                WMWorkspaceOpenStatus.CorruptManifest,
                null,
                [new WMWorkspaceRecoveryIssue(
                    WMWorkspaceOpenStatus.CorruptManifest,
                    ex.Message,
                    [],
                    [WMWorkspaceRecoveryAction.DiscardSession])]);
        }
    }

    public async Task<WMWorkspaceOpenResult> RecoverAsync(
        string sessionId,
        WMWorkspaceRecoveryAction action,
        IReadOnlyList<string> affectedIds,
        CancellationToken token = default)
    {
        ValidateSessionId(sessionId);
        ArgumentNullException.ThrowIfNull(affectedIds);
        if (action == WMWorkspaceRecoveryAction.DiscardSession)
        {
            await DeleteAsync(sessionId).ConfigureAwait(false);
            return new WMWorkspaceOpenResult(WMWorkspaceOpenStatus.Missing, null, []);
        }

        var opened = await OpenAsync(sessionId, token).ConfigureAwait(false);
        var source = opened.Session;
        if (source is null) return opened;
        var affected = affectedIds.ToHashSet(StringComparer.Ordinal);
        WMWorkspaceSession recovered;
        if (action == WMWorkspaceRecoveryAction.RemoveAffectedMedia)
        {
            var removedMediaIds = source.MediaCatalog
                .Where(media => affected.Contains(media.Artifact.Id)
                                || affected.Contains(media.Id)
                                || affected.Contains(source.CurrentArtifactIdsByMediaId
                                    .GetValueOrDefault(media.Id, string.Empty)))
                .Select(media => media.Id)
                .ToHashSet(StringComparer.Ordinal);
            var catalog = source.MediaCatalog.Where(media => !removedMediaIds.Contains(media.Id)).ToArray();
            var catalogArtifactIds = catalog.Select(media => media.Artifact.Id).ToHashSet(StringComparer.Ordinal);
            recovered = source with
            {
                Revision = checked(source.Revision + 1),
                MediaCatalog = catalog,
                Media = source.Media.Where(media => !removedMediaIds.Contains(media.Id)).ToArray(),
                ActiveMediaIds = source.ActiveMediaIds.Where(id => !removedMediaIds.Contains(id)).ToArray(),
                SelectedMediaIds = source.SelectedMediaIds.Where(id => !removedMediaIds.Contains(id)).ToArray(),
                CurrentMediaId = removedMediaIds.Contains(source.CurrentMediaId ?? string.Empty)
                    ? catalog.FirstOrDefault()?.Id
                    : source.CurrentMediaId,
                Artifacts = source.Artifacts.Where(artifact =>
                    !affected.Contains(artifact.Id) || catalogArtifactIds.Contains(artifact.Id)).ToArray(),
                CurrentArtifactIdsByMediaId = source.CurrentArtifactIdsByMediaId
                    .Where(pair => !removedMediaIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                CropSettingsByMediaId = source.CropSettingsByMediaId
                    .Where(pair => !removedMediaIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                Transactions = RemoveMediaFromTransactions(source.Transactions, removedMediaIds),
                ActiveJobCheckpoint = null
            };
        }
        else if (action == WMWorkspaceRecoveryAction.RemoveTemplateOperation)
        {
            recovered = source with
            {
                Revision = checked(source.Revision + 1),
                Operations = source.Operations.Where(operation => operation.Kind != WMImageOperationKind.Template).ToArray(),
                Transactions = source.Transactions.Select(RemoveTemplateOperations).Where(HasTransactionEffect).ToArray(),
                TemplateId = null,
                TemplateIdsByMediaId = new Dictionary<string, string?>()
            };
            recovered = recovered with
            {
                HistoryCursor = Math.Min(recovered.HistoryCursor, recovered.Transactions.Count)
            };
        }
        else
        {
            return opened;
        }

        await SaveAsync(NormalizeSessionGraph(recovered), token).ConfigureAwait(false);
        return await OpenAsync(sessionId, token).ConfigureAwait(false);
    }

    public async Task SaveAsync(WMWorkspaceSession session, CancellationToken token = default)
    {
        ValidateSessionId(session.Id);
        var path = ManifestPath(session.Id);
        var saveLock = ManifestLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await saveLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var persisted = await ReadManifestAsync(path, token).ConfigureAwait(false);
            if (persisted is { SchemaVersion: >= WMWorkspaceSession.CurrentSchemaVersion }
                && session.Revision <= persisted.Revision)
                throw new WMStaleSessionRevisionException(session.Id, session.Revision, persisted.Revision);

            var normalized = NormalizeSessionGraph(session);
            await SaveCoreAsync(normalized with
            {
                SchemaVersion = WMWorkspaceSession.CurrentSchemaVersion,
                SelectedMediaIds = NormalizeSelectedMediaIds(normalized)
            }, token).ConfigureAwait(false);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private async Task SaveCoreAsync(WMWorkspaceSession session, CancellationToken token)
    {
        var directory = SessionDirectory(session.Id);
        Directory.CreateDirectory(directory);
        var path = ManifestPath(session.Id);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        var persisted = session with
        {
            UpdatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
        };
        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, persisted, JsonOptions, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            if (File.Exists(path))
                File.Copy(path, Path.Combine(directory, LastGoodManifestFileName), overwrite: true);
            File.Move(temporary, path, true);
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    public async Task DeleteAsync(string sessionId)
    {
        ValidateSessionId(sessionId);
        var path = ManifestPath(sessionId);
        var sessionLock = ManifestLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try { TryDeleteDirectory(SessionDirectory(sessionId)); }
        finally { sessionLock.Release(); }
    }

    public async Task<IReadOnlyList<WMWorkspaceSession>> ListRecentAsync(
        int take = 5,
        CancellationToken token = default)
    {
        await CleanupExpiredAsync(token).ConfigureAwait(false);
        var sessions = new List<WMWorkspaceSession>();
        foreach (var directory in new DirectoryInfo(root)
                     .EnumerateDirectories()
                     .OrderByDescending(item => item.LastWriteTimeUtc))
        {
            token.ThrowIfCancellationRequested();
            var opened = await OpenAsync(directory.Name, token).ConfigureAwait(false);
            if (opened.IsOpened) sessions.Add(opened.Session!);
            if (sessions.Count >= Math.Clamp(take, 1, 20)) break;
        }
        return sessions;
    }

    public Task CleanupExpiredAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(root)) return Task.CompletedTask;
        foreach (var directory in new DirectoryInfo(root).EnumerateDirectories())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var manifest = Path.Combine(directory.FullName, ManifestFileName);
                if (!File.Exists(manifest)
                    && directory.CreationTimeUtc < DateTime.UtcNow.AddHours(-24))
                {
                    TryDeleteDirectory(directory.FullName);
                    continue;
                }
                if (!ActiveSessionLeases.ContainsKey(manifest)
                    && File.Exists(manifest)
                    && File.GetLastWriteTimeUtc(manifest) < DateTime.UtcNow.AddHours(-24))
                    TryDeleteDirectory(directory.FullName);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return Task.CompletedTask;
    }

    private sealed class SessionLease(string manifestPath) : IDisposable
    {
        private string? path = manifestPath;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref path, null);
            if (current is null) return;
            while (ActiveSessionLeases.TryGetValue(current, out var count))
            {
                if (count <= 1)
                {
                    if (((ICollection<KeyValuePair<string, int>>)ActiveSessionLeases)
                        .Remove(new KeyValuePair<string, int>(current, count))) return;
                }
                else if (ActiveSessionLeases.TryUpdate(current, count - 1, count))
                {
                    return;
                }
            }
        }
    }

    private string SessionDirectory(string id) => Path.Combine(root, id);
    private string ManifestPath(string id) => Path.Combine(SessionDirectory(id), ManifestFileName);

    private static void ValidateSessionId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)
            || id.Length > 64
            || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("工作台会话 ID 无效。", nameof(id));
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private async Task<WMWorkspaceSession> MigrateLegacyAsync(
        WMWorkspaceSession session,
        string manifestPath,
        CancellationToken token)
    {
        var sessionLock = ManifestLocks.GetOrAdd(manifestPath, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = await ReadManifestAsync(manifestPath, token).ConfigureAwait(false);
            if (current is { SchemaVersion: WMWorkspaceSession.CurrentSchemaVersion }) return current;

            var sourceSchema = Math.Clamp(session.SchemaVersion, 1, 4);
            var backup = Path.Combine(
                Path.GetDirectoryName(manifestPath)!, $"manifest.v{sourceSchema}.bak");
            if (!File.Exists(backup)) File.Copy(manifestPath, backup, overwrite: false);
            var baselineOperations = session.SchemaVersion == 1
                ? new List<WMImageOperation>()
                : session.Operations.ToList();
            var catalog = session.MediaCatalog.Count > 0 ? session.MediaCatalog : session.Media;
            var activeMediaIds = catalog.Select(item => item.Id).ToArray();
            var inputArtifactIds = catalog.Select(item => item.Artifact.Id).ToArray();
            if (session.SchemaVersion == 1 && !string.IsNullOrWhiteSpace(session.TemplateId))
            {
                var configPath = Path.Combine(
                    Global.AppPath.TemplatesFolder, session.TemplateId, "config.json");
                if (File.Exists(configPath))
                {
                    var canvasJson = await File.ReadAllTextAsync(configPath, token).ConfigureAwait(false);
                    baselineOperations.Add(WMImageOperation.Create(
                        WMImageOperationKind.Template,
                        inputArtifactIds,
                        inputArtifactIds.Select(_ => Guid.NewGuid().ToString("N")),
                        new WMWorkspaceTemplateSelection(session.TemplateId, canvasJson)));
                }
            }
            if (session.SchemaVersion == 1 && session.ColorRecipe is not null)
            {
                baselineOperations.Add(WMImageOperation.Create(
                    WMImageOperationKind.ColorGrade,
                    inputArtifactIds,
                    inputArtifactIds.Select(_ => Guid.NewGuid().ToString("N")),
                    session.ColorRecipe));
            }
            baselineOperations = UpgradeColorOperations(baselineOperations).ToList();
            var migratedTransactions = session.SchemaVersion == 1
                ? []
                : UpgradeColorTransactions(NormalizeTransactions(session.Transactions, activeMediaIds));
            var hasLegacyColor = session.ColorRecipe is not null
                || session.ColorRecipesByMediaId.Values.Any(recipe => recipe is not null)
                || baselineOperations.Any(operation => operation.Kind == WMImageOperationKind.ColorGrade)
                || migratedTransactions.Any(transaction => transaction.EffectiveOperations.Any(
                    operation => operation.Kind == WMImageOperationKind.ColorGrade));
            var migratedRecipe = UpgradeRecipe(session.ColorRecipe);
            var migratedRecipeOverrides = session.ColorRecipesByMediaId.ToDictionary(
                pair => pair.Key,
                pair => UpgradeRecipe(pair.Value),
                StringComparer.Ordinal);
            var migrated = session with
            {
                SchemaVersion = WMWorkspaceSession.CurrentSchemaVersion,
                Revision = Math.Max(0, session.Revision),
                Media = catalog,
                MediaCatalog = catalog,
                ActiveMediaIds = activeMediaIds,
                SelectedMediaIds = session.SelectedMediaIds.Count > 0
                    ? session.SelectedMediaIds.Where(activeMediaIds.Contains).ToArray()
                    : catalog.Where(item => item.IsSelected).Select(item => item.Id).ToArray(),
                Artifacts = catalog.Select(item => item.Artifact)
                    .Concat(session.Artifacts)
                    .GroupBy(item => item.Id, StringComparer.Ordinal)
                    .Select(group => group.Last())
                    .ToArray(),
                CurrentArtifactIdsByMediaId = catalog.ToDictionary(
                    item => item.Id,
                    item => !hasLegacyColor
                            && session.CurrentArtifactIdsByMediaId.TryGetValue(item.Id, out var currentArtifactId)
                        ? currentArtifactId
                        : item.Artifact.Id,
                    StringComparer.Ordinal),
                Operations = baselineOperations,
                Transactions = migratedTransactions,
                ColorRecipe = migratedRecipe,
                ColorRecipesByMediaId = migratedRecipeOverrides,
                HistoryCursor = session.SchemaVersion == 1
                    ? 0
                    : Math.Clamp(session.HistoryCursor, 0, session.Transactions.Count),
                RequiredFeatures = session.RequiredFeatures
                    .Distinct()
                    .ToArray(),
                ActiveJobCheckpoint = session.ActiveJobCheckpoint
            };
            migrated = NormalizeSessionGraph(migrated);
            await SaveCoreAsync(migrated, token).ConfigureAwait(false);
            return migrated;
        }
        finally
        {
            sessionLock.Release();
        }
    }

    private static async Task<WMWorkspaceSession?> ReadManifestAsync(
        string path,
        CancellationToken token)
    {
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            return await JsonSerializer.DeserializeAsync<WMWorkspaceSession>(stream, JsonOptions, token)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<(WMWorkspaceSession? Session, bool Restored)> TryRestoreBackupAsync(
        string manifestPath,
        CancellationToken token)
    {
        var directory = Path.GetDirectoryName(manifestPath)!;
        var candidates = new[]
        {
            Path.Combine(directory, LastGoodManifestFileName),
            Path.Combine(directory, "manifest.v3.bak"),
            Path.Combine(directory, "manifest.v2.bak"),
            Path.Combine(directory, "manifest.v1.bak")
        };
        foreach (var candidate in candidates)
        {
            var backup = await ReadManifestAsync(candidate, token).ConfigureAwait(false);
            if (backup is null || backup.SchemaVersion > WMWorkspaceSession.CurrentSchemaVersion) continue;
            var temporary = manifestPath + $".{Guid.NewGuid():N}.recovery.tmp";
            try
            {
                File.Copy(candidate, temporary, overwrite: false);
                File.Move(temporary, manifestPath, true);
                return (backup, true);
            }
            finally
            {
                TryDeleteFile(temporary);
            }
        }
        return (null, false);
    }

    private static IReadOnlyList<string> FindMissingTemplateResources(WMWorkspaceSession session)
    {
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in session.Operations.Where(item => item.Kind == WMImageOperationKind.Template))
        {
            try
            {
                var selection = JsonSerializer.Deserialize<WMWorkspaceTemplateSelection>(
                    operation.ParametersJson,
                    JsonOptions);
                if (string.IsNullOrWhiteSpace(selection?.CanvasJson)) continue;
                var canvas = Global.ReadConfig(selection.CanvasJson);
                foreach (var control in Global.EnumerateControls(canvas))
                {
                    var path = control switch
                    {
                        WMLogo logo => logo.Path,
                        WMContainer container => container.Path,
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(path)
                        && Path.IsPathFullyQualified(path)
                        && !File.Exists(path))
                        missing.Add(operation.Id);
                }
            }
            catch
            {
                missing.Add(operation.Id);
            }
        }
        return missing.ToArray();
    }

    private static IReadOnlyList<WMWorkspaceTransaction> RemoveMediaFromTransactions(
        IReadOnlyList<WMWorkspaceTransaction> transactions,
        IReadOnlySet<string> removedMediaIds) =>
        transactions.Select(transaction => transaction with
            {
                Assignments = transaction.Assignments
                    .Select(assignment => assignment with
                    {
                        MediaIds = assignment.MediaIds.Where(id => !removedMediaIds.Contains(id)).ToArray()
                    })
                    .Where(assignment => assignment.MediaIds.Count > 0 && assignment.Operations.Count > 0)
                    .ToArray(),
                AddedMediaIds = transaction.AddedMediaIds.Where(id => !removedMediaIds.Contains(id)).ToArray(),
                RemovedMediaIds = transaction.RemovedMediaIds.Where(id => !removedMediaIds.Contains(id)).ToArray()
            })
            .Where(HasTransactionEffect)
            .ToArray();

    private static WMWorkspaceTransaction RemoveTemplateOperations(WMWorkspaceTransaction transaction) =>
        transaction with
        {
            Assignments = transaction.Assignments
                .Select(assignment => assignment with
                {
                    Operations = assignment.Operations
                        .Where(operation => operation.Kind != WMImageOperationKind.Template)
                        .ToArray()
                })
                .Where(assignment => assignment.Operations.Count > 0)
                .ToArray(),
            Operations = transaction.Operations
                .Where(operation => operation.Kind != WMImageOperationKind.Template)
                .ToArray()
        };

    private static bool HasTransactionEffect(WMWorkspaceTransaction transaction) =>
        transaction.Assignments.Any(assignment => assignment.Operations.Count > 0)
        || transaction.Operations.Count > 0
        || transaction.AddedMediaIds.Count > 0
        || transaction.RemovedMediaIds.Count > 0;

    private static IReadOnlyList<string> NormalizeSelectedMediaIds(WMWorkspaceSession session)
    {
        var available = session.Media.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var selected = session.SelectedMediaIds
            .Where(available.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selected.Length > 0) return selected;
        return session.Media.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
    }

    private static IReadOnlyList<WMWorkspaceTransaction> NormalizeTransactions(
        IReadOnlyList<WMWorkspaceTransaction> transactions,
        IReadOnlyList<string> fallbackMediaIds) =>
        transactions.Select(transaction => transaction.Assignments.Count > 0
            ? transaction with { Operations = [] }
            : transaction with
            {
                Assignments = transaction.Operations.Count == 0
                    ? []
                    : [new WMWorkspaceOperationAssignment(fallbackMediaIds, transaction.Operations)],
                Operations = []
            }).ToArray();

    private static WMWorkspaceSession NormalizeSessionGraph(WMWorkspaceSession session)
    {
        var catalog = session.MediaCatalog
            .Concat(session.Media)
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var catalogIds = catalog.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var activeIds = session.SchemaVersion >= WMWorkspaceSession.CurrentSchemaVersion
            ? session.ActiveMediaIds.Where(catalogIds.Contains).Distinct(StringComparer.Ordinal).ToArray()
            : session.Media.Select(item => item.Id).Where(catalogIds.Contains).Distinct(StringComparer.Ordinal).ToArray();
        var activeSet = activeIds.ToHashSet(StringComparer.Ordinal);
        var activeMedia = catalog.Where(item => activeSet.Contains(item.Id)).ToArray();
        var selected = session.SelectedMediaIds.Where(activeSet.Contains).Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0)
            selected = activeMedia.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
        var currentMediaId = activeSet.Contains(session.CurrentMediaId ?? string.Empty)
            ? session.CurrentMediaId
            : activeMedia.FirstOrDefault()?.Id;
        var normalized = session with
        {
            SchemaVersion = WMWorkspaceSession.CurrentSchemaVersion,
            MediaCatalog = catalog,
            ActiveMediaIds = activeIds,
            Media = activeMedia,
            SelectedMediaIds = selected,
            CurrentMediaId = currentMediaId,
            Transactions = NormalizeTransactions(session.Transactions, activeIds),
            HistoryCursor = Math.Clamp(session.HistoryCursor, 0, session.Transactions.Count),
            RequiredFeatures = session.RequiredFeatures.Distinct().ToArray()
        };
        return NormalizeArtifactGraph(normalized);
    }

    private static WMWorkspaceSession NormalizeArtifactGraph(WMWorkspaceSession session)
    {
        var artifacts = session.MediaCatalog.Select(item => item.Artifact)
            .Concat(session.Artifacts)
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        var artifactIds = artifacts.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var media in session.MediaCatalog)
        {
            current[media.Id] = session.CurrentArtifactIdsByMediaId.TryGetValue(media.Id, out var artifactId)
                                && artifactIds.Contains(artifactId)
                ? artifactId
                : media.Artifact.Id;
        }
        return session with
        {
            Artifacts = artifacts,
            CurrentArtifactIdsByMediaId = current
        };
    }

    private static WMColorRecipe? UpgradeRecipe(WMColorRecipe? recipe)
    {
        if (recipe is null) return null;
        var upgraded = WMColorRecipeSnapshot.Copy(recipe)!;
        upgraded.UpgradeToCurrentSchema();
        return upgraded;
    }

    private static IReadOnlyList<WMImageOperation> UpgradeColorOperations(
        IEnumerable<WMImageOperation> operations) => operations.Select(operation =>
    {
        if (operation.Kind != WMImageOperationKind.ColorGrade) return operation;
        try
        {
            var recipe = JsonSerializer.Deserialize<WMColorRecipe>(operation.ParametersJson, JsonOptions);
            recipe = UpgradeRecipe(recipe);
            return recipe is null
                ? operation
                : operation with { ParametersJson = JsonSerializer.Serialize(recipe, JsonOptions) };
        }
        catch (JsonException)
        {
            return operation;
        }
    }).ToArray();

    private static IReadOnlyList<WMWorkspaceTransaction> UpgradeColorTransactions(
        IReadOnlyList<WMWorkspaceTransaction> transactions) => transactions.Select(transaction => transaction with
    {
        Assignments = transaction.Assignments.Select(assignment => assignment with
        {
            Operations = UpgradeColorOperations(assignment.Operations)
        }).ToArray(),
        Operations = UpgradeColorOperations(transaction.Operations)
    }).ToArray();
}

public sealed class WMStaleSessionRevisionException(
    string sessionId,
    long attemptedRevision,
    long persistedRevision)
    : InvalidOperationException(
        $"会话 {sessionId} 的修订 {attemptedRevision} 已过期；当前持久化修订为 {persistedRevision}。")
{
    public string SessionId { get; } = sessionId;
    public long AttemptedRevision { get; } = attemptedRevision;
    public long PersistedRevision { get; } = persistedRevision;
}
