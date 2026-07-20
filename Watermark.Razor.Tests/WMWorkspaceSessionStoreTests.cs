using SkiaSharp;
using System.Text.Json.Nodes;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWorkspaceSessionStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "watermark-workspace-store-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateEmpty_OpensDesktopWorkspaceWithoutChangingRegularCreateValidation()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());

        var id = await store.CreateEmptyAsync(WMWorkspaceMode.Template, "/mac");
        var session = Opened(await store.OpenAsync(id));

        Assert.Equal(WMWorkspaceMode.Template, session.Mode);
        Assert.Equal("/mac", session.ReturnPath);
        Assert.Empty(session.Media);
        Assert.Empty(session.Artifacts);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(
            new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [])));
    }

    [Fact]
    public async Task Create_StagesAndDecodesSourceExactlyOnce()
    {
        var metrics = new WMWorkspacePerformanceCounters();
        var store = CreateStore(metrics);
        var source = CreateImage("source.png", 1800, 1200);

        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(
            WMWorkspaceMode.Template, [source]));
        var session = Opened(await store.OpenAsync(id));

        Assert.Single(session.Media);
        Assert.Single(session.Artifacts);
        Assert.Equal(
            session.Media[0].Artifact.Id,
            session.CurrentArtifactIdsByMediaId[session.Media[0].Id]);
        Assert.NotEqual(source, session.Media[0].Artifact.FilePath);
        Assert.True(File.Exists(session.Media[0].Artifact.PreviewPath));
        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.Decode]);
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.Scale]);
        Assert.Equal(1, snapshot.Calls[WMWorkspaceMetricStage.Encode]);
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(root, id), "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task StreamPickerImport_OpensOnceAndStagesDirectlyIntoSession()
    {
        var metrics = new WMWorkspacePerformanceCounters();
        var store = CreateStore(metrics);
        var source = CreateImage("stream-source.png", 640, 480);
        var bytes = await File.ReadAllBytesAsync(source);
        var openCalls = 0;
        var picked = new WMPhotoImportSource(
            "picked.png",
            _ =>
            {
                Interlocked.Increment(ref openCalls);
                return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
            });

        var id = await store.CreateAsync(WMWorkspaceMode.Template, [picked]);
        var session = Opened(await store.OpenAsync(id));

        Assert.Equal(1, openCalls);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root, id, "sources")));
        Assert.Equal(1, metrics.Snapshot().Calls[WMWorkspaceMetricStage.Decode]);
    }

    [Fact]
    public async Task TemplateDesign_ReturnPathIsSanitizedAndPersistsForRecovery()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());

        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(
            WMWorkspaceMode.TemplateDesign,
            [],
            "template-1",
            "/templates?tab=favorites"));
        var restored = Opened(await store.OpenAsync(id));

        Assert.Equal("/templates?tab=favorites", restored.ReturnPath);

        var unsafeId = await store.CreateAsync(new WMWorkspaceCreateRequest(
            WMWorkspaceMode.TemplateDesign,
            [],
            "template-2",
            "https://example.com/templates"));
        var sanitized = Opened(await store.OpenAsync(unsafeId));

        Assert.Equal("/create", sanitized.ReturnPath);
    }

    [Fact]
    public async Task CorruptedManifest_ReturnsRecoveryFailureInsteadOfThrowing()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("corrupt.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(
            WMWorkspaceMode.ColorGrade, [source]));
        await File.WriteAllTextAsync(Path.Combine(root, id, "manifest.json"), "{broken");

        var result = await store.OpenAsync(id);

        Assert.Equal(WMWorkspaceOpenStatus.CorruptManifest, result.Status);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task CleanupExpired_RemovesOnlyOldSessionDirectory()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("expiry.png", 320, 240);
        var oldId = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var activeId = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        File.SetLastWriteTimeUtc(Path.Combine(root, oldId, "manifest.json"), DateTime.UtcNow.AddHours(-25));

        await store.CleanupExpiredAsync();

        Assert.False(Directory.Exists(Path.Combine(root, oldId)));
        Assert.True(Directory.Exists(Path.Combine(root, activeId)));
    }

    [Fact]
    public async Task CleanupExpired_DoesNotRemoveActivelyLeasedSession()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("leased-expiry.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var manifest = Path.Combine(root, id, "manifest.json");
        File.SetLastWriteTimeUtc(manifest, DateTime.UtcNow.AddHours(-25));

        using (store.AcquireLease(id))
        {
            await store.CleanupExpiredAsync();
            Assert.True(Directory.Exists(Path.Combine(root, id)));
        }

        await store.CleanupExpiredAsync();
        Assert.False(Directory.Exists(Path.Combine(root, id)));
    }

    [Fact]
    public async Task OlderRevision_CannotOverwriteNewerManifest()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("revision.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var opened = Opened(await store.OpenAsync(id));
        var newer = opened with { Revision = 2, Mode = WMWorkspaceMode.ColorGrade };
        await store.SaveAsync(newer);

        var stale = opened with { Revision = 1, Mode = WMWorkspaceMode.MultiFrame };
        await Assert.ThrowsAsync<WMStaleSessionRevisionException>(() => store.SaveAsync(stale));

        var restored = Opened(await store.OpenAsync(id));
        Assert.Equal(2, restored.Revision);
        Assert.Equal(WMWorkspaceMode.ColorGrade, restored.Mode);
    }

    [Fact]
    public async Task StableDerivedArtifact_AndCurrentMapping_SurviveProcessRestore()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("artifact-graph.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.MultiFrame, [source]));
        var opened = Opened(await store.OpenAsync(id));
        var derivedPath = Path.Combine(root, id, "artifacts", "derived.png");
        Directory.CreateDirectory(Path.GetDirectoryName(derivedPath)!);
        File.Copy(opened.Media[0].Artifact.PreviewPath!, derivedPath);
        var derived = opened.Media[0].Artifact with
        {
            Id = "derived-artifact",
            FilePath = derivedPath,
            PreviewPath = null,
            ParentArtifactIds = [opened.Media[0].Artifact.Id],
            SourceOperation = WMImageOperationKind.MultiFrameStack
        };
        var updated = opened with
        {
            Revision = opened.Revision + 1,
            Artifacts = opened.Artifacts.Append(derived).ToArray(),
            CurrentArtifactIdsByMediaId = new Dictionary<string, string>
            {
                [opened.Media[0].Id] = derived.Id
            }
        };

        await store.SaveAsync(updated);
        var restored = Opened(await CreateStore(new WMWorkspacePerformanceCounters()).OpenAsync(id));

        Assert.Contains(restored.Artifacts, item => item.Id == derived.Id);
        Assert.Equal(derived.Id, restored.CurrentArtifactIdsByMediaId[opened.Media[0].Id]);
    }

    [Fact]
    public async Task SeparateStoreInstance_CannotOverwriteSameOrNewerRevision()
    {
        var firstStore = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("cross-instance-revision.png", 320, 240);
        var id = await firstStore.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var opened = Opened(await firstStore.OpenAsync(id));
        await firstStore.SaveAsync(opened with { Revision = 2, Mode = WMWorkspaceMode.ColorGrade });

        var secondStore = CreateStore(new WMWorkspacePerformanceCounters());
        await Assert.ThrowsAsync<WMStaleSessionRevisionException>(() => secondStore.SaveAsync(
            opened with { Revision = 2, Mode = WMWorkspaceMode.MultiFrame }));
        await Assert.ThrowsAsync<WMStaleSessionRevisionException>(() => secondStore.SaveAsync(
            opened with { Revision = 1, Mode = WMWorkspaceMode.MultiFrame }));

        var restored = Opened(await secondStore.OpenAsync(id));
        Assert.Equal(2, restored.Revision);
        Assert.Equal(WMWorkspaceMode.ColorGrade, restored.Mode);
    }

    [Fact]
    public async Task V1Manifest_MigratesOnceAndKeepsBackup()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("migration.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var manifestPath = Path.Combine(root, id, "manifest.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        json["schemaVersion"] = 1;
        json.Remove("selectedMediaIds");
        await File.WriteAllTextAsync(manifestPath, json.ToJsonString());

        var migrated = Opened(await store.OpenAsync(id));

        Assert.Equal(WMWorkspaceSession.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(migrated.Media.Select(item => item.Id), migrated.SelectedMediaIds);
        var backup = Path.Combine(root, id, "manifest.v1.bak");
        Assert.True(File.Exists(backup));
        var backupWriteTime = File.GetLastWriteTimeUtc(backup);

        var reopened = Opened(await store.OpenAsync(id));
        Assert.Equal(WMWorkspaceSession.CurrentSchemaVersion, reopened.SchemaVersion);
        Assert.Equal(backupWriteTime, File.GetLastWriteTimeUtc(backup));
    }

    [Fact]
    public async Task TemplateDraft_PersistsHistoryAndDirtyStateInManifestV4()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(
            WMWorkspaceMode.TemplateDesign,
            [],
            "template-a"));
        var opened = Opened(await store.OpenAsync(id));
        var editor = WMTemplateEditorState.Create(new WMCanvas
        {
            ID = "template-a",
            Name = "original"
        });
        editor.Mutate("rename", () => editor.Draft.Name = "draft");
        Assert.True(editor.TryExportDraftState(out var editorDraft));
        var updated = opened with
        {
            Revision = opened.Revision + 1,
            TemplateDraft = new WMWorkspaceTemplateDraft(
                "template-a",
                Global.CanvasSerialize(new WMCanvas { ID = "template-a", Name = "original" }),
                editorDraft!,
                DateTime.UtcNow)
        };

        await store.SaveAsync(updated);
        var restored = Opened(await store.OpenAsync(id));

        Assert.NotNull(restored.TemplateDraft);
        var restoredEditor = WMTemplateEditorState.Restore(
            new WMCanvas { ID = "template-a", Name = "original" },
            restored.TemplateDraft!.EditorState);
        Assert.Equal("draft", restoredEditor.Draft.Name);
        Assert.True(restoredEditor.IsDirty);
    }

    [Fact]
    public async Task Create_WritesCurrentCatalogAndActiveProjection()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("v3-create.png", 320, 240);

        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var session = Opened(await store.OpenAsync(id));

        Assert.Equal(WMWorkspaceSession.CurrentSchemaVersion, session.SchemaVersion);
        Assert.Equal(session.Media.Select(item => item.Id), session.MediaCatalog.Select(item => item.Id));
        Assert.Equal(session.Media.Select(item => item.Id), session.ActiveMediaIds);
    }

    [Fact]
    public async Task V2Manifest_MigratesCatalogAndKeepsV2Backup()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("v2-migration.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var manifestPath = Path.Combine(root, id, "manifest.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        json["schemaVersion"] = 2;
        json.Remove("mediaCatalog");
        json.Remove("activeMediaIds");
        await File.WriteAllTextAsync(manifestPath, json.ToJsonString());

        var migrated = Opened(await store.OpenAsync(id));

        Assert.Equal(WMWorkspaceSession.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(migrated.Media.Select(item => item.Id), migrated.MediaCatalog.Select(item => item.Id));
        Assert.Equal(migrated.Media.Select(item => item.Id), migrated.ActiveMediaIds);
        Assert.True(File.Exists(Path.Combine(root, id, "manifest.v2.bak")));
    }

    [Fact]
    public async Task V2LegacyTransaction_GainsExplicitAssignmentTargets()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("v2-transaction.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.ColorGrade, [source]));
        var opened = Opened(await store.OpenAsync(id));
        var operation = WMImageOperation.Create(
            WMImageOperationKind.ColorGrade,
            [opened.Media[0].Artifact.Id],
            ["legacy-output"],
            new WMColorRecipe());
        var legacy = opened with
        {
            SchemaVersion = 2,
            MediaCatalog = [],
            ActiveMediaIds = [],
            Transactions =
            [
                new WMWorkspaceTransaction
                {
                    Id = "legacy-transaction",
                    Label = "旧调色",
                    Operations = [operation],
                    CreatedAtUtc = DateTime.UtcNow
                }
            ],
            HistoryCursor = 1
        };
        var manifestPath = Path.Combine(root, id, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(legacy));

        var migrated = Opened(await store.OpenAsync(id));

        var assignment = Assert.Single(Assert.Single(migrated.Transactions).Assignments);
        Assert.Equal(migrated.ActiveMediaIds, assignment.MediaIds);
        Assert.Equal(operation.Id, Assert.Single(assignment.Operations).Id);
        Assert.Empty(migrated.Transactions[0].Operations);
    }

    [Fact]
    public async Task InactiveCatalogMedia_SurvivesSaveAndRestore()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("inactive.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var opened = Opened(await store.OpenAsync(id));
        var updated = opened with
        {
            Revision = opened.Revision + 1,
            ActiveMediaIds = [],
            Media = [],
            SelectedMediaIds = [],
            CurrentMediaId = null
        };

        await store.SaveAsync(updated);
        var restored = Opened(await store.OpenAsync(id));

        Assert.Empty(restored.Media);
        Assert.Empty(restored.ActiveMediaIds);
        Assert.Single(restored.MediaCatalog);
        Assert.Single(restored.Artifacts);
    }

    [Fact]
    public async Task AtomicSave_LeavesNoTemporaryManifestFiles()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("atomic-v3.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var opened = Opened(await store.OpenAsync(id));

        await store.SaveAsync(opened with { Revision = opened.Revision + 1, Mode = WMWorkspaceMode.ColorGrade });

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, id), "manifest*.tmp", SearchOption.TopDirectoryOnly));
        Assert.Equal(WMWorkspaceMode.ColorGrade, Opened(await store.OpenAsync(id)).Mode);
    }

    [Fact]
    public async Task V3Manifest_MigratesRequiredFeaturesAndKeepsV3Backup()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("v3-migration.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.MultiFrame, [source]));
        var manifestPath = Path.Combine(root, id, "manifest.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        json["schemaVersion"] = 3;
        json.Remove("requiredFeatures");
        json.Remove("activeJobCheckpoint");
        await File.WriteAllTextAsync(manifestPath, json.ToJsonString());

        var migrated = Opened(await store.OpenAsync(id));

        Assert.Equal(WMWorkspaceSession.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Empty(migrated.RequiredFeatures);
        Assert.Null(migrated.ActiveJobCheckpoint);
        Assert.True(File.Exists(Path.Combine(root, id, "manifest.v3.bak")));
    }

    [Fact]
    public async Task CorruptedPrimary_RestoresLastGoodBackupWithoutLosingFiles()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("backup-recovery.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var opened = Opened(await store.OpenAsync(id));
        await store.SaveAsync(opened with { Revision = 1, Mode = WMWorkspaceMode.ColorGrade });
        var manifestPath = Path.Combine(root, id, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{broken");

        var recovered = await store.OpenAsync(id);

        Assert.Equal(WMWorkspaceOpenStatus.Opened, recovered.Status);
        Assert.NotNull(recovered.Session);
        Assert.Contains(recovered.Issues, issue => issue.Status == WMWorkspaceOpenStatus.CorruptManifest);
        Assert.Equal(WMWorkspaceMode.Template, recovered.Session!.Mode);
        Assert.NotNull(JsonNode.Parse(await File.ReadAllTextAsync(manifestPath)));
    }

    [Fact]
    public async Task NewerManifestVersion_IsPreservedAndReportedUnsupported()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("future-version.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [source]));
        var manifestPath = Path.Combine(root, id, "manifest.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        json["schemaVersion"] = WMWorkspaceSession.CurrentSchemaVersion + 1;
        var futureContent = json.ToJsonString();
        await File.WriteAllTextAsync(manifestPath, futureContent);

        var result = await store.OpenAsync(id);

        Assert.Equal(WMWorkspaceOpenStatus.UnsupportedVersion, result.Status);
        Assert.Null(result.Session);
        Assert.Equal(futureContent, await File.ReadAllTextAsync(manifestPath));
    }

    [Fact]
    public async Task MissingMedia_CanBeRemovedThroughExplicitRecoveryAction()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var first = CreateImage("missing-first.png", 320, 240);
        var second = CreateImage("missing-second.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.Template, [first, second]));
        var opened = Opened(await store.OpenAsync(id));
        var missing = opened.Media[0];
        File.Delete(missing.Artifact.FilePath);
        if (missing.Artifact.PreviewPath is { Length: > 0 } preview) File.Delete(preview);

        var failedOpen = await store.OpenAsync(id);
        Assert.Equal(WMWorkspaceOpenStatus.MissingMedia, failedOpen.Status);
        var affected = Assert.Single(failedOpen.Issues).AffectedIds;

        var recovered = await store.RecoverAsync(
            id,
            WMWorkspaceRecoveryAction.RemoveAffectedMedia,
            affected);

        var session = Opened(recovered);
        Assert.DoesNotContain(session.MediaCatalog, media => media.Id == missing.Id);
        Assert.Single(session.Media);
    }

    [Fact]
    public async Task RunningJob_IsProjectedAsInterruptedAndRetryableAfterOpen()
    {
        var store = CreateStore(new WMWorkspacePerformanceCounters());
        var source = CreateImage("interrupted-job.png", 320, 240);
        var id = await store.CreateAsync(new WMWorkspaceCreateRequest(WMWorkspaceMode.MultiFrame, [source]));
        var opened = Opened(await store.OpenAsync(id));
        var now = DateTime.UtcNow;
        await store.SaveAsync(opened with
        {
            Revision = opened.Revision + 1,
            ActiveJobCheckpoint = new WMWorkspaceJobCheckpoint(
                "job-1",
                WMWorkspaceJobKind.MultiFrame,
                WMWorkspaceJobStatus.Running,
                "{}",
                [],
                now,
                now)
        });

        var restored = Opened(await store.OpenAsync(id));

        Assert.Equal(WMWorkspaceJobStatus.Interrupted, restored.ActiveJobCheckpoint?.Status);
        Assert.NotNull(restored.ActiveJobCheckpoint?.ErrorMessage);
    }

    private WMWorkspaceSessionStore CreateStore(IWMWorkspacePerformanceCounters metrics)
    {
        var capabilities = new WMStaticImagingCapabilities(WMImagingCapabilities.MobileDisabled);
        var profiles = new TestExecutionProfileProvider(capabilities);
        var importer = new WMImageImportService(
            new WMLocalSourceStager(copyLocalSources: true),
            new WMMetadataExtractorReader(),
            metrics);
        return new WMWorkspaceSessionStore(importer, profiles, root);
    }

    private static WMWorkspaceSession Opened(WMWorkspaceOpenResult result)
    {
        Assert.Equal(WMWorkspaceOpenStatus.Opened, result.Status);
        return Assert.IsType<WMWorkspaceSession>(result.Session);
    }

    private string CreateImage(string name, int width, int height)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }

    private sealed class TestExecutionProfileProvider(IWMImagingCapabilities capabilities)
        : IWMExecutionProfileProvider
    {
        public WMOperationExecutionOptions GetInteractiveProfile() => new()
        {
            MaxConcurrentImages = 1,
            PreviewMaxEdge = 1600,
            PreviewDecodeConcurrency = 1
        };

        public WMImagingCapabilities GetImagingCapabilities() => capabilities.Current;
    }
}
