using SkiaSharp;
using Watermark.Razor.Components.Mac;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWorkspaceControllerTests : IDisposable
{
    private readonly List<string> templateDirectories = [];
    private readonly string previewPath = Path.Combine(
        Path.GetTempPath(), $"watermark-controller-preview-{Guid.NewGuid():N}.jpg");

    [Fact]
    public async Task OpeningAnotherSession_ThenApplyingTemplate_KeepsPreviewVersionsIncreasing()
    {
        CreateTemplate("template-a");
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var sessions = new MultiSessionStore(
            Session("first", "first-media"),
            Session("second", "second-media"));
        var renderer = new StrictlyIncreasingRenderCoordinator(previewPath);
        var controller = new WMWorkspaceController(
            sessions,
            renderer,
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);

        Assert.True(await controller.OpenAsync("first"));
        Assert.True(await controller.OpenAsync("second"));
        await controller.ApplyTemplateAsync("template-a");

        Assert.Equal([1L, 2L, 3L], renderer.RequestedVersions);
        Assert.Equal(3, controller.State.PreviewVersion);
        Assert.Equal("template-a", controller.State.TemplateId);
        Assert.Equal(WMWorkspaceActivity.PreviewReady, controller.State.Activity);
        Assert.Null(controller.State.ErrorMessage);
    }

    [Fact]
    public async Task OpeningSessionCreatedFromTemplateCard_PersistsSnapshotBeforeFirstPreview()
    {
        CreateTemplate("template-card");
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var store = new RecordingSessionStore(
            Session("template-card-session", "template-card-media") with
            {
                TemplateId = "template-card"
            });
        var controller = new WMWorkspaceController(
            store,
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);

        Assert.True(await controller.OpenAsync("template-card-session"));

        Assert.Null(store.Saved!.TemplateId);
        Assert.Single(store.Saved.Transactions);
        Assert.Equal("template-card", controller.State.TemplateId);
        Assert.True(controller.State.CanUndo);
        Assert.Equal(1, controller.State.PreviewVersion);

        await controller.UndoAsync();

        Assert.Null(controller.State.TemplateId);
    }

    [Fact]
    public async Task UpdateColorGradeAsync_PreservesNewGradeWhenCompatibilityCopyIsStale()
    {
        var initial = new WMColorRecipe { Name = "mobile" };
        initial.Grade.Exposure = .2f;
        initial.UserAdjustments = new WMColorGradeSettings { Exposure = .2f };
        var session = new WMWorkspaceSession
        {
            Id = "color-session",
            Mode = WMWorkspaceMode.ColorGrade,
            ColorRecipe = initial
        };
        var store = new RecordingSessionStore(session);
        var controller = CreateController(store);

        Assert.True(await controller.OpenAsync(session.Id));

        // JSON/session cloning produces two settings instances. This mirrors a
        // mobile slider editing Grade without also editing UserAdjustments.
        var edited = Clone(controller.State.ColorRecipe!);
        edited.Grade.Exposure = 1.5f;
        Assert.Equal(.2f, edited.UserAdjustments!.Exposure);

        await controller.UpdateColorGradeAsync(edited, createHistoryEntry: false);

        Assert.Equal(1.5f, controller.State.ColorRecipe!.Grade.Exposure);
        Assert.Equal(1.5f, controller.State.ColorRecipe.UserAdjustments!.Exposure);
        Assert.Null(store.Saved);

        await controller.UpdateColorGradeAsync(edited, createHistoryEntry: true);

        Assert.Equal(1.5f, store.Saved!.ColorRecipe!.Grade.Exposure);
        Assert.Equal(1.5f, store.Saved.ColorRecipe.UserAdjustments!.Exposure);
    }

    [Fact]
    public async Task UpdateColorGradeAsync_UsesLatestGradeAcrossConsecutiveCommits()
    {
        var initial = new WMColorRecipe { Name = "mobile" };
        initial.Normalize();
        var store = new RecordingSessionStore(new WMWorkspaceSession
        {
            Id = "consecutive-color-session",
            Mode = WMWorkspaceMode.ColorGrade,
            ColorRecipe = initial
        });
        var controller = CreateController(store);
        Assert.True(await controller.OpenAsync("consecutive-color-session"));

        var first = Clone(controller.State.ColorRecipe!);
        first.Grade.Contrast = 25;
        await controller.UpdateColorGradeAsync(first, createHistoryEntry: true);

        var second = Clone(controller.State.ColorRecipe!);
        second.Grade.Contrast = -30;
        Assert.Equal(25, second.UserAdjustments!.Contrast);
        await controller.UpdateColorGradeAsync(second, createHistoryEntry: true);

        Assert.Equal(-30, controller.State.ColorRecipe!.Grade.Contrast);
        Assert.Equal(-30, controller.State.ColorRecipe.UserAdjustments!.Contrast);
        Assert.Equal(-30, store.Saved!.ColorRecipe!.Grade.Contrast);
    }

    [Fact]
    public async Task FiftyTemporaryColorInputs_PublishOnlyMonotonicLatestStateWithoutSaving()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var initial = Session("rapid-color", "rapid-media") with
        {
            Mode = WMWorkspaceMode.ColorGrade,
            ColorRecipe = new WMColorRecipe { Name = "rapid" }
        };
        var store = new RecordingSessionStore(initial);
        var renderer = new StrictlyIncreasingRenderCoordinator(previewPath);
        var controller = new WMWorkspaceController(
            store,
            renderer,
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync(initial.Id));

        var tasks = new List<Task>();
        for (var value = 1; value <= 50; value++)
        {
            var recipe = Clone(controller.State.ColorRecipe!);
            recipe.Grade.Contrast = value;
            tasks.Add(controller.UpdateColorGradeAsync(recipe, createHistoryEntry: false));
        }
        await Task.WhenAll(tasks);

        Assert.Equal(50, controller.State.ColorRecipe!.Grade.Contrast);
        Assert.Null(store.Saved);
        Assert.Equal(51, controller.State.PreviewVersion);
        Assert.Equal(Enumerable.Range(1, 51).Select(value => (long)value), renderer.RequestedVersions);
    }

    [Fact]
    public async Task CloseAsync_WaitsForInFlightOriginalPublication_AndLeavesNoLease()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var registry = new BlockingOriginalObjectUrlRegistry();
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(Session("close-publication", "close-media")),
            new StrictlyIncreasingRenderCoordinator(previewPath),
            registry,
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync("close-publication"));

        var compareTask = controller.SetCompareOriginalAsync(true);
        await registry.OriginalPublishStarted.Task;
        var closeTask = controller.CloseAsync();

        Assert.False(closeTask.IsCompleted);
        registry.AllowOriginalPublish.SetResult();
        await Task.WhenAll(compareTask, closeTask);

        Assert.Equal(0, registry.ActiveLeaseCount);
    }

    [Fact]
    public async Task MediaPreviewUrls_ArePublishedOncePerMedia_ReusedAndReleasedOnClose()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var registry = new TrackingOwnerObjectUrlRegistry();
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(SessionWithTwoMedia("media-preview-session")),
            new StrictlyIncreasingRenderCoordinator(previewPath),
            registry,
            CreatePreviewService(),
            null!,
            null!);

        Assert.True(await controller.OpenAsync("media-preview-session"));

        Assert.StartsWith("blob:", controller.GetMediaPreviewUrl("media-1"), StringComparison.Ordinal);
        Assert.StartsWith("blob:", controller.GetMediaPreviewUrl("media-2"), StringComparison.Ordinal);
        Assert.Equal(1, registry.PublishCount("workspace:media-preview:media-1"));
        Assert.Equal(1, registry.PublishCount("workspace:media-preview:media-2"));

        await controller.SetModeAsync(WMWorkspaceMode.Collage);

        Assert.Equal(1, registry.PublishCount("workspace:media-preview:media-1"));
        Assert.Equal(1, registry.PublishCount("workspace:media-preview:media-2"));

        await controller.CloseAsync();

        Assert.Equal(0, registry.ActiveLeaseCount);
        Assert.Null(controller.GetMediaPreviewUrl("media-1"));
        Assert.Null(controller.GetMediaPreviewUrl("media-2"));
    }

    [Fact]
    public async Task PreviewArtifactLease_IsReplacedAndReleasedWhenControllerCloses()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var cache = new TrackingArtifactCache();
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(Session("artifact-lease", "artifact-media")),
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!,
            artifactCache: cache);

        Assert.True(await controller.OpenAsync("artifact-lease"));
        Assert.Equal(1, cache.ActiveLeaseCount);

        var recipe = new WMColorRecipe { Name = "temporary" };
        recipe.Grade.Contrast = 12;
        await controller.UpdateColorGradeAsync(recipe, createHistoryEntry: false);

        Assert.Equal(1, cache.ActiveLeaseCount);
        Assert.Equal(2, cache.AcquireCount);

        await controller.CloseAsync();

        Assert.Equal(0, cache.ActiveLeaseCount);
    }

    [Fact]
    public async Task CurrentAndSelectedScopes_KeepIndependentTemplateAndColorState()
    {
        CreateTemplate("template-a");
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var initial = SessionWithTwoMedia("scoped-session");
        var store = new RecordingSessionStore(initial);
        var controller = new WMWorkspaceController(
            store,
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync(initial.Id));

        await controller.ToggleMediaSelectionAsync("media-2");
        await controller.ApplyTemplateAsync("template-a", WMApplyScope.Selected);
        await controller.SelectMediaAsync("media-2");

        Assert.Null(controller.State.TemplateId);
        Assert.Equal("template-a", store.Saved!.TemplateIdsByMediaId["media-1"]);
        Assert.False(store.Saved.Media.Single(item => item.Id == "media-2").IsSelected);

        var recipe = new WMColorRecipe { Name = "selected-only" };
        recipe.Grade.Exposure = 1.25f;
        await controller.UpdateColorGradeAsync(recipe, true, WMApplyScope.Selected);
        Assert.Null(controller.State.ColorRecipe);

        await controller.SelectMediaAsync("media-1");
        Assert.Equal("template-a", controller.State.TemplateId);
        Assert.Equal(1.25f, controller.State.ColorRecipe!.Grade.Exposure);
    }

    [Fact]
    public async Task BatchExport_PreservesSuccessfulItems_WhenOneDestinationFails()
    {
        var exportRoot = Path.Combine(Path.GetTempPath(), $"watermark-batch-export-{Guid.NewGuid():N}");
        try
        {
            var previews = Path.Combine(exportRoot, "session", "previews");
            Directory.CreateDirectory(previews);
            var firstPath = Path.Combine(previews, "first.png");
            var secondPath = Path.Combine(previews, "second.png");
            WriteImage(firstPath, SKColors.CornflowerBlue);
            WriteImage(secondPath, SKColors.IndianRed);
            var initial = SessionForPaths("batch-export", firstPath, secondPath);
            var store = new RecordingSessionStore(initial);
            var scheduler = new WMProcessingScheduler();
            var renderer = new WMTemplateRenderer(new WatermarkHelper());
            var profiles = new TestExecutionProfileProvider();
            var exportService = new WMFullResolutionRenderService(
                new WMFastJpegExportService(
                    renderer,
                    new WMColorGradeOperationProcessor(scheduler),
                    scheduler),
                profiles);
            var controller = new WMWorkspaceController(
                store,
                new StrictlyIncreasingRenderCoordinator(firstPath),
                new NoopObjectUrlRegistry(),
                CreatePreviewService(profiles),
                exportService,
                new FailingSecondExportSink());
            Assert.True(await controller.OpenAsync(initial.Id));

            var result = await controller.ExportAsync(new WMExportRequest(
                ["media-1", "media-2"],
                WMExportFormat.Jpeg8,
                1920,
                92,
                WMExportDestinationKind.PlatformDefault));

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(WMExportItemStatus.Succeeded, result.Items[0].Status);
            Assert.Equal(WMExportItemStatus.Failed, result.Items[1].Status);
            Assert.True(File.Exists(result.Items[0].RenderedPath));
            Assert.Contains("模拟保存失败", result.Items[1].ErrorMessage);
            Assert.Equal(WMWorkspaceActivity.Completed, controller.State.Activity);
        }
        finally
        {
            try { if (Directory.Exists(exportRoot)) Directory.Delete(exportRoot, true); } catch { }
        }
    }

    [Fact]
    public async Task MultiFrameResult_BecomesStableArtifact_AndUndoRedoSwitchesCurrentArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), $"watermark-multiframe-controller-{Guid.NewGuid():N}");
        try
        {
            var previewDirectory = Path.Combine(root, "session", "previews");
            Directory.CreateDirectory(previewDirectory);
            var paths = Enumerable.Range(1, 3)
                .Select(index => Path.Combine(previewDirectory, $"frame-{index}.png"))
                .ToArray();
            WriteImage(paths[0], SKColors.Navy);
            WriteImage(paths[1], SKColors.DarkBlue);
            WriteImage(paths[2], SKColors.MidnightBlue);
            WMWorkspaceMedia CreateMedia(int index) => new()
            {
                Id = $"media-{index + 1}",
                DisplayName = $"frame-{index + 1}.png",
                OriginalReference = paths[index],
                Artifact = new WMImageArtifact
                {
                    Id = $"artifact-{index + 1}",
                    FilePath = paths[index],
                    PreviewPath = paths[index],
                    ContentHash = $"artifact-{index + 1}",
                    Width = 32,
                    Height = 24
                }
            };
            var media = Enumerable.Range(0, 3).Select(CreateMedia).ToArray();
            var initial = new WMWorkspaceSession
            {
                Id = "multi-frame-session",
                Mode = WMWorkspaceMode.MultiFrame,
                Media = media,
                Artifacts = media.Select(item => item.Artifact).ToArray(),
                CurrentArtifactIdsByMediaId = media.ToDictionary(
                    item => item.Id, item => item.Artifact.Id, StringComparer.Ordinal),
                SelectedMediaIds = media.Select(item => item.Id).ToArray(),
                CurrentMediaId = media[0].Id
            };
            var store = new RecordingSessionStore(initial);
            var profiles = new TestExecutionProfileProvider();
            var engine = new RecordingStackEngine();
            var controller = new WMWorkspaceController(
                store,
                new StrictlyIncreasingRenderCoordinator(paths[0]),
                new NoopObjectUrlRegistry(),
                CreatePreviewService(profiles),
                null!,
                null!,
                imagingCapabilities: new AvailableImagingCapabilityProvider(),
                imageStackEngine: engine,
                executionProfiles: profiles);
            Assert.True(await controller.OpenAsync(initial.Id));

            await controller.UpdateMultiFrameDraftAsync(
                controller.State.MultiFrameTool.Draft with
                {
                    Advanced = new WMMultiFrameAdvancedDraft(
                        WMAlignmentMode.Stars,
                        WMReductionMode.SigmaClippedMean,
                        AutomaticReduction: false,
                        SigmaLow: 2.1,
                        SigmaHigh: 3.4,
                        SigmaIterations: 4,
                        ReferenceMediaId: media[1].Id)
                });
            await controller.ExecuteMultiFrameAsync(
                cancellationToken: CancellationToken.None);

            var output = Assert.Single(engine.Outputs);
            var settings = Assert.Single(engine.Settings);
            Assert.Equal(WMAlignmentMode.Stars, settings.Alignment);
            Assert.Equal(WMReductionMode.SigmaClippedMean, settings.Reduction);
            Assert.False(settings.AutomaticReduction);
            Assert.Equal(2.1, settings.SigmaLow);
            Assert.Equal(3.4, settings.SigmaHigh);
            Assert.Equal(4, settings.SigmaIterations);
            Assert.Equal(media[1].Artifact.Id, settings.ReferenceArtifactId);
            Assert.Equal(output.Id, controller.State.CurrentMedia!.Artifact.Id);
            Assert.Contains(store.Saved!.Artifacts, item => item.Id == output.Id);
            Assert.Equal(output.Id, store.Saved.CurrentArtifactIdsByMediaId[media[0].Id]);
            var transaction = Assert.Single(store.Saved.Transactions);
            var selection = System.Text.Json.JsonSerializer.Deserialize<WMWorkspaceMultiFrameSelection>(
                Assert.Single(transaction.EffectiveOperations).ParametersJson);
            Assert.Equal(media[0].Id, selection!.TargetMediaId);

            await controller.UndoAsync();
            Assert.Equal(media[0].Artifact.Id, controller.State.CurrentMedia!.Artifact.Id);

            await controller.RedoAsync();
            Assert.Equal(output.Id, controller.State.CurrentMedia!.Artifact.Id);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task MultiFramePreview_IsLatestWins_AndCanceledResultCannotPublish()
    {
        var root = Path.Combine(Path.GetTempPath(), $"watermark-multiframe-preview-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var firstPath = Path.Combine(root, "frame-1.png");
            var secondPath = Path.Combine(root, "frame-2.png");
            WriteImage(firstPath, SKColors.Navy);
            WriteImage(secondPath, SKColors.DarkBlue);
            var initial = SessionForPaths("multi-preview-latest", firstPath, secondPath) with
            {
                Mode = WMWorkspaceMode.MultiFrame,
                MultiFrameConfiguration = new WMMultiFrameDraft(
                    WMStackMode.StarTrail,
                    new Dictionary<string, WMFrameRole?>
                    {
                        ["media-1"] = WMFrameRole.Light,
                        ["media-2"] = WMFrameRole.Light
                    },
                    NormalizeExposure: false,
                    RepairHotPixels: true,
                    AutoCrop: false)
            };
            var profiles = new TestExecutionProfileProvider();
            var urls = new TrackingOwnerObjectUrlRegistry();
            var engine = new LatestWinsStackEngine();
            var controller = new WMWorkspaceController(
                new RecordingSessionStore(initial),
                new StrictlyIncreasingRenderCoordinator(firstPath),
                urls,
                CreatePreviewService(profiles),
                null!,
                null!,
                imagingCapabilities: new AvailableImagingCapabilityProvider(),
                imageStackEngine: engine,
                executionProfiles: profiles);
            Assert.True(await controller.OpenAsync(initial.Id));
            var publishedBeforePreview = urls.PublishCountByPrefix("workspace:preview:");

            var first = controller.PreviewMultiFrameAsync(CancellationToken.None);
            await engine.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var second = controller.PreviewMultiFrameAsync(CancellationToken.None);
            await Task.WhenAll(first, second);

            Assert.Equal(2, engine.CallCount);
            Assert.Equal(1, engine.CanceledCount);
            Assert.Equal(publishedBeforePreview + 1, urls.PublishCountByPrefix("workspace:preview:"));
            Assert.Equal("preview-output-2", controller.State.PreviewPresentation.BaseFingerprint);
            Assert.Equal(WMWorkspaceActivity.PreviewReady, controller.State.Activity);
            Assert.False(controller.State.MultiFrameTool.IsBusy);
            await controller.CloseAsync();
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task ReopenedSession_RestoresPersistentHistoryAndTemplateSnapshots()
    {
        CreateTemplate("template-history-a");
        CreateTemplate("template-history-b");
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var store = new MultiSessionStore(Session("persistent-history", "history-media"));
        var first = new WMWorkspaceController(
            store,
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await first.OpenAsync("persistent-history"));
        await first.ApplyTemplateAsync("template-history-a");
        await first.ApplyTemplateAsync("template-history-b");
        await first.CloseAsync();

        foreach (var directory in templateDirectories.ToArray())
            if (Directory.Exists(directory)) Directory.Delete(directory, true);

        var reopened = new WMWorkspaceController(
            store,
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await reopened.OpenAsync("persistent-history"));
        Assert.Equal("template-history-b", reopened.State.TemplateId);
        Assert.True(reopened.State.CanUndo);

        await reopened.UndoAsync();

        Assert.Equal("template-history-a", reopened.State.TemplateId);
        Assert.Null(reopened.State.ErrorMessage);
    }

    [Fact]
    public async Task ApplyingTemplate_CopiesReferencedResourcesIntoSessionSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"watermark-template-snapshot-{Guid.NewGuid():N}");
        var previewDirectory = Path.Combine(root, "session", "previews");
        Directory.CreateDirectory(previewDirectory);
        var sourcePath = Path.Combine(previewDirectory, "source.png");
        WriteImage(sourcePath, SKColors.CadetBlue);
        const string templateId = "template-with-resource";
        var templateDirectory = Path.Combine(Global.AppPath.TemplatesFolder, templateId);
        Directory.CreateDirectory(templateDirectory);
        templateDirectories.Add(templateDirectory);
        var originalResource = Path.Combine(templateDirectory, "badge.png");
        WriteImage(originalResource, SKColors.Gold);
        var container = new WMContainer();
        container.Controls.Add(new WMLogo { Path = "badge.png" });
        var canvas = new WMCanvas { ID = templateId, Name = templateId, Children = [container] };
        await File.WriteAllTextAsync(
            Path.Combine(templateDirectory, "config.json"),
            Global.CanvasSerialize(canvas));

        try
        {
            var store = new RecordingSessionStore(SessionForPaths(
                "resource-snapshot", sourcePath, sourcePath) with
            {
                Media = [SessionForPaths("resource-snapshot", sourcePath, sourcePath).Media[0]],
                SelectedMediaIds = ["media-1"]
            });
            var controller = new WMWorkspaceController(
                store,
                new StrictlyIncreasingRenderCoordinator(sourcePath),
                new NoopObjectUrlRegistry(),
                CreatePreviewService(),
                null!,
                null!);
            Assert.True(await controller.OpenAsync("resource-snapshot"));

            await controller.ApplyTemplateAsync(templateId);

            var operation = Assert.Single(Assert.Single(store.Saved!.Transactions).EffectiveOperations);
            var selection = System.Text.Json.JsonSerializer
                .Deserialize<WMWorkspaceTemplateSelection>(operation.ParametersJson)!;
            var snappedCanvas = Global.ReadConfig(selection.CanvasJson!);
            var snappedLogo = Assert.Single(Global.EnumerateControls(snappedCanvas).OfType<WMLogo>());
            Assert.StartsWith(Path.Combine(root, "session", "snapshots"), snappedLogo.Path);
            Assert.True(File.Exists(snappedLogo.Path));

            Directory.Delete(templateDirectory, true);
            Assert.True(File.Exists(snappedLogo.Path));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task PreviewTemplate_IsTransientAndDoesNotSaveManifest()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var store = new RecordingSessionStore(Session("template-preview", "media-preview"));
        var controller = new WMWorkspaceController(store, new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(), CreatePreviewService(), null!, null!);
        Assert.True(await controller.OpenAsync("template-preview"));

        await controller.PreviewTemplateAsync(
            new WMWorkspaceTemplateEdit("draft-template", Global.CanvasSerialize(new WMCanvas())),
            WMApplyScope.Current);

        Assert.True(controller.State.HasTransientEdits);
        Assert.Equal("draft-template", controller.State.TemplateId);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task SwitchingTemplate_InvalidatesPreviousLayoutUntilLatestPreviewPublishes()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var renderer = new BlockingSecondRenderCoordinator(previewPath);
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(Session("template-layout-switch", "template-layout-media")),
            renderer,
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync("template-layout-switch"));
        Assert.NotNull(controller.State.TemplateLayout);
        Assert.Equal(10, controller.State.TemplateLayout!.CanvasWidth);

        var canvas = new WMCanvas { ID = "template-layout-next", Name = "next" };
        var switching = controller.PreviewTemplateAsync(
            new WMWorkspaceTemplateEdit(canvas.ID, Global.CanvasSerialize(canvas)),
            WMApplyScope.Current);
        await renderer.SecondPreviewQueued.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(canvas.ID, controller.State.TemplateEdit?.TemplateId);
        Assert.Equal(WMWorkspaceActivity.Previewing, controller.State.Activity);
        Assert.Null(controller.State.TemplateLayout);

        renderer.ReleaseSecondPreview();
        await switching;

        Assert.NotNull(controller.State.TemplateLayout);
        Assert.Equal(20, controller.State.TemplateLayout!.CanvasWidth);
        Assert.Equal(10, controller.State.TemplateLayout.CanvasHeight);
    }

    [Fact]
    public async Task PreviewTemplateWithoutCanvasJson_ResolvesSnapshotBeforeRendering()
    {
        const string templateId = "preview-template-without-snapshot";
        CreateTemplate(templateId);
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var store = new RecordingSessionStore(Session("template-preview-fallback", "media-preview-fallback"));
        var controller = new WMWorkspaceController(
            store,
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync("template-preview-fallback"));

        await controller.PreviewTemplateAsync(
            new WMWorkspaceTemplateEdit(templateId, null),
            WMApplyScope.Current);

        Assert.Equal(templateId, controller.State.TemplateId);
        Assert.Equal(templateId, controller.State.TemplateEdit?.TemplateId);
        Assert.False(string.IsNullOrWhiteSpace(controller.State.TemplateEdit?.CanvasJson));
        Assert.Equal(templateId, Global.ReadConfig(controller.State.TemplateEdit!.CanvasJson!).ID);
        Assert.True(controller.State.HasTransientEdits);
        Assert.Null(controller.State.ErrorMessage);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task PreviewTemplate_OverridesExplicitNoTemplateProjectionInRenderPlan()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var initial = Session("template-preview-projection", "media-preview-projection");
        var committedOperation = WMImageOperation.Create(
            WMImageOperationKind.Template,
            [initial.Media[0].Artifact.Id],
            ["no-template-output"],
            new WMWorkspaceTemplateSelection(null, null));
        var committedTransaction = new WMWorkspaceTransaction
        {
            Id = "committed-template-transaction",
            Label = "应用模板",
            Assignments =
            [
                new WMWorkspaceOperationAssignment(
                    [initial.Media[0].Id],
                    [committedOperation])
            ],
            CreatedAtUtc = DateTime.UnixEpoch
        };
        initial = initial with
        {
            Transactions = [committedTransaction],
            HistoryCursor = 1,
            Operations = [committedOperation],
            TemplateIdsByMediaId = new Dictionary<string, string?>
            {
                [initial.Media[0].Id] = null
            }
        };
        var compiler = new RecordingRenderPlanCompiler();
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(initial),
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!,
            renderPlanCompiler: compiler);
        Assert.True(await controller.OpenAsync(initial.Id));
        var previewCanvas = new WMCanvas { ID = "preview-template", Name = "preview-template" };

        await controller.PreviewTemplateAsync(
            new WMWorkspaceTemplateEdit(previewCanvas.ID, Global.CanvasSerialize(previewCanvas)),
            WMApplyScope.Current);

        var templateStep = Assert.Single(
            compiler.Plans[^1].Steps,
            step => step.Operation.Kind == WMImageOperationKind.Template);
        using var parameters = System.Text.Json.JsonDocument.Parse(templateStep.Operation.ParametersJson);
        var canvasJson = parameters.RootElement.GetProperty(nameof(WMTemplateOperationSettings.CanvasJson)).GetString();
        Assert.Equal(previewCanvas.ID, Global.ReadConfig(canvasJson!).ID);
    }

    [Fact]
    public async Task DiscardTransientTemplate_RestoresCommittedProjection()
    {
        CreateTemplate("committed-template");
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var store = new RecordingSessionStore(Session("discard-template", "media-discard"));
        var controller = new WMWorkspaceController(store, new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(), CreatePreviewService(), null!, null!);
        Assert.True(await controller.OpenAsync("discard-template"));
        await controller.ApplyTemplateAsync("committed-template", WMApplyScope.Current);
        var committed = store.Saved;
        await controller.PreviewTemplateAsync(
            new WMWorkspaceTemplateEdit("temporary-template", Global.CanvasSerialize(new WMCanvas())),
            WMApplyScope.Current);

        await controller.DiscardTransientEditsAsync();

        Assert.False(controller.State.HasTransientEdits);
        Assert.Equal("committed-template", controller.State.TemplateId);
        Assert.Same(committed, store.Saved);
    }

    [Fact]
    public async Task CommitTemplate_RecordsExplicitMediaAssignment()
    {
        CreateTemplate("assigned-template");
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var store = new RecordingSessionStore(SessionWithTwoMedia("assigned-session"));
        var controller = new WMWorkspaceController(store, new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(), CreatePreviewService(), null!, null!);
        Assert.True(await controller.OpenAsync("assigned-session"));
        await controller.ToggleMediaSelectionAsync("media-2");

        await controller.ApplyTemplateAsync("assigned-template", WMApplyScope.Selected);

        var transaction = Assert.Single(store.Saved!.Transactions);
        var assignment = Assert.Single(transaction.Assignments);
        Assert.Equal(["media-1"], assignment.MediaIds);
        Assert.Single(assignment.Operations);
        Assert.Empty(transaction.Operations);
    }

    [Fact]
    public async Task RemoveMedia_UndoAndRedoProjectsActiveCatalog()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var store = new MultiSessionStore(SessionWithTwoMedia("remove-session"));
        var controller = new WMWorkspaceController(store, new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(), CreatePreviewService(), null!, null!);
        Assert.True(await controller.OpenAsync("remove-session"));

        await controller.RemoveMediaAsync(["media-2"]);
        Assert.Equal(["media-1"], controller.State.Media.Select(item => item.Id));
        await controller.UndoAsync();
        Assert.Equal(["media-1", "media-2"], controller.State.Media.Select(item => item.Id));
        await controller.RedoAsync();
        Assert.Equal(["media-1"], controller.State.Media.Select(item => item.Id));
    }

    [Fact]
    public async Task RemovingCurrentMedia_SelectsRemainingMedia()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var initial = SessionWithTwoMedia("remove-current") with { CurrentMediaId = "media-2" };
        var store = new RecordingSessionStore(initial);
        var controller = new WMWorkspaceController(store, new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(), CreatePreviewService(), null!, null!);
        Assert.True(await controller.OpenAsync(initial.Id));

        await controller.RemoveMediaAsync(["media-2"]);

        Assert.Equal("media-1", controller.State.CurrentMediaId);
        Assert.Equal(["media-1", "media-2"], store.Saved!.MediaCatalog.Select(item => item.Id));
        Assert.Equal(["media-1"], store.Saved.ActiveMediaIds);
    }

    [Fact]
    public async Task SetHistoryCursor_JumpsWithoutCreatingTransaction()
    {
        CreateTemplate("cursor-a");
        CreateTemplate("cursor-b");
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var store = new RecordingSessionStore(Session("cursor-session", "cursor-media"));
        var controller = new WMWorkspaceController(store, new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(), CreatePreviewService(), null!, null!);
        Assert.True(await controller.OpenAsync("cursor-session"));
        await controller.ApplyTemplateAsync("cursor-a");
        await controller.ApplyTemplateAsync("cursor-b");

        await controller.SetHistoryCursorAsync(1);

        Assert.Equal("cursor-a", controller.State.TemplateId);
        Assert.Equal(2, store.Saved!.Transactions.Count);
        Assert.Equal(1, store.Saved.HistoryCursor);
    }

    [Fact]
    public async Task NewCommitAfterUndo_TruncatesRedoBranch()
    {
        CreateTemplate("branch-a");
        CreateTemplate("branch-b");
        CreateTemplate("branch-c");
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var store = new RecordingSessionStore(Session("branch-session", "branch-media"));
        var controller = new WMWorkspaceController(store, new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(), CreatePreviewService(), null!, null!);
        Assert.True(await controller.OpenAsync("branch-session"));
        await controller.ApplyTemplateAsync("branch-a");
        await controller.ApplyTemplateAsync("branch-b");
        await controller.UndoAsync();

        await controller.ApplyTemplateAsync("branch-c");

        Assert.Equal(2, store.Saved!.Transactions.Count);
        Assert.Equal(2, store.Saved.HistoryCursor);
        Assert.Equal("branch-c", controller.State.TemplateId);
        Assert.False(controller.State.CanRedo);
    }

    [Fact]
    public async Task CreateDerivedMedia_IsOneUndoableStructuralTransaction()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var store = new RecordingSessionStore(SessionWithTwoMedia("derived-session"));
        var derivedProcessor = new RecordingDerivedProcessor(previewPath);
        var controller = new WMWorkspaceController(store, new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(), CreatePreviewService(), null!, null!,
            derivedMediaProcessor: derivedProcessor);
        Assert.True(await controller.OpenAsync("derived-session"));

        var derivedId = await controller.CreateDerivedMediaAsync(new WMDerivedMediaRequest(
            WMDerivedMediaKind.Collage,
            ["media-1", "media-2"],
            "创建拼图",
            new WMCollageSettings(["media-1", "media-2"], WMCollageDirection.Horizontal),
            "拼图.png",
            true));

        Assert.Equal(derivedId, controller.State.CurrentMediaId);
        Assert.Single(store.Saved!.Transactions);
        Assert.Equal([derivedId], store.Saved.Transactions[0].AddedMediaIds);
        await controller.UndoAsync();
        Assert.DoesNotContain(controller.State.Media, item => item.Id == derivedId);
        Assert.Contains(store.Saved.Artifacts, item => item.Id == derivedProcessor.ArtifactId);
    }

    [Fact]
    public async Task DiscardTransientColor_RestoresCommittedRecipeWithoutSaving()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var committed = new WMColorRecipe { Name = "committed" };
        committed.Grade.Exposure = .5f;
        var store = new RecordingSessionStore(Session("discard-color", "color-media") with { ColorRecipe = committed });
        var controller = new WMWorkspaceController(store, new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(), CreatePreviewService(), null!, null!);
        Assert.True(await controller.OpenAsync("discard-color"));
        var draft = Clone(controller.State.ColorRecipe!);
        draft.Grade.Exposure = 2;
        await controller.UpdateColorGradeAsync(draft, false);

        await controller.DiscardTransientEditsAsync();

        Assert.Equal(.5f, controller.State.ColorRecipe!.Grade.Exposure);
        Assert.False(controller.State.HasTransientEdits);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task SwitchingMedia_KeepsUnappliedColorDraftWithoutCommittingIt()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var initial = SessionWithTwoMedia("color-draft-switch") with
        {
            Mode = WMWorkspaceMode.ColorGrade
        };
        var store = new RecordingSessionStore(initial);
        var controller = new WMWorkspaceController(
            store,
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync(initial.Id));
        var draft = Clone(controller.State.ColorGradeTool.Draft);
        draft.Grade.Exposure = 1.75f;
        await controller.UpdateColorGradeAsync(draft, createHistoryEntry: false, WMApplyScope.Current);

        await controller.SelectMediaAsync("media-2");

        Assert.Equal("media-2", controller.State.CurrentMediaId);
        Assert.Equal(1.75f, controller.State.ColorGradeTool.Draft.Grade.Exposure);
        Assert.Equal(1.75f, controller.State.ColorRecipe!.Grade.Exposure);
        Assert.True(controller.State.HasTransientEdits);
        Assert.Equal(WMWorkspaceMode.ColorGrade, controller.State.TransientEditMode);
        Assert.Empty(store.Saved!.ColorRecipesByMediaId);
        Assert.Empty(store.Saved.Transactions);

        await controller.CommitColorDraftAsync(WMApplyScope.Current);

        Assert.False(store.Saved.ColorRecipesByMediaId.ContainsKey("media-1"));
        Assert.Equal(1.75f, store.Saved.ColorRecipesByMediaId["media-2"]!.Grade.Exposure);
        Assert.Equal(["media-2"], Assert.Single(store.Saved.Transactions).Assignments.Single().MediaIds);
    }

    [Fact]
    public async Task RepeatedMediaSwitches_SupersedeBlockedUnappliedColorPreview()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var renderer = new BlockingSecondRenderCoordinator(previewPath);
        var initial = SessionWithTwoMedia("color-draft-latest-switch") with
        {
            Mode = WMWorkspaceMode.ColorGrade
        };
        var store = new RecordingSessionStore(initial);
        var controller = new WMWorkspaceController(
            store,
            renderer,
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync(initial.Id));
        var draft = Clone(controller.State.ColorGradeTool.Draft);
        draft.Grade.Shadows = 35;
        await controller.UpdateColorDraftAsync(draft);
        await renderer.SecondPreviewQueued.WaitAsync(TimeSpan.FromSeconds(3));

        await controller.SelectMediaAsync("media-2");
        await controller.SelectMediaAsync("media-1");

        Assert.Equal("media-1", controller.State.CurrentMediaId);
        Assert.Equal(35, controller.State.ColorGradeTool.Draft.Grade.Shadows);
        Assert.True(controller.State.HasTransientEdits);
        Assert.Empty(store.Saved!.Transactions);
        Assert.Empty(store.Saved.ColorRecipesByMediaId);

        renderer.ReleaseSecondPreview();
        await controller.CloseAsync();
    }

    [Fact]
    public async Task HistoryProjection_TracksAppliedAndRedoItems()
    {
        CreateTemplate("history-a");
        CreateTemplate("history-b");
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(Session("history-projection", "history-media")),
            new StrictlyIncreasingRenderCoordinator(previewPath), new NoopObjectUrlRegistry(),
            CreatePreviewService(), null!, null!);
        Assert.True(await controller.OpenAsync("history-projection"));
        await controller.ApplyTemplateAsync("history-a");
        await controller.ApplyTemplateAsync("history-b");
        await controller.UndoAsync();

        Assert.Equal(2, controller.State.History.Count);
        Assert.True(controller.State.History[0].IsApplied);
        Assert.False(controller.State.History[1].IsApplied);
        Assert.Equal(1, controller.State.HistoryCursor);
    }

    [Fact]
    public async Task ModeSwitch_UpdatesSessionWithoutQueueingImageRender()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var coordinator = new StrictlyIncreasingRenderCoordinator(previewPath);
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(Session("mode-switch", "media")),
            coordinator,
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync("mode-switch"));
        var renderCount = coordinator.RequestedVersions.Count;

        await controller.SetModeAsync(WMWorkspaceMode.ColorGrade);

        Assert.Equal(WMWorkspaceMode.ColorGrade, controller.State.Mode);
        Assert.Equal(renderCount, coordinator.RequestedVersions.Count);
    }

    [Fact]
    public async Task MissingOcio_BlocksColorModeAndEditsWithoutBlockingOtherModes()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(Session("ocio-unavailable", "media")),
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!,
            colorEngine: new WMUnsupportedColorEngine("ABI 4 OCIO capability is missing"));
        Assert.True(await controller.OpenAsync("ocio-unavailable"));

        await controller.SetModeAsync(WMWorkspaceMode.ColorGrade);

        Assert.NotEqual(WMWorkspaceMode.ColorGrade, controller.State.Mode);
        Assert.Contains("OpenColorIO 2.5.2", controller.State.ErrorMessage);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            controller.UpdateColorGradeAsync(new WMColorRecipe(), createHistoryEntry: false));

        await controller.SetModeAsync(WMWorkspaceMode.Template);
        Assert.Equal(WMWorkspaceMode.Template, controller.State.Mode);
    }

    [Fact]
    public async Task WebGlDrafts_DoNotQueueCpuPreview_AndOnlyLastProgramPublishes()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var coordinator = new StrictlyIncreasingRenderCoordinator(previewPath);
        var compiler = new ImmediateColorPipelineCompiler();
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(Session("gpu-drafts", "media") with
                { Mode = WMWorkspaceMode.ColorGrade }),
            coordinator,
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!,
            colorPipelineCompiler: compiler);
        Assert.True(await controller.OpenAsync("gpu-drafts"));
        await controller.SetColorPreviewCapabilityAsync(new WMColorPreviewCapability(
            true,
            Max3DTextureSize: 4096,
            PipelineVersion: WMColorPipelineVersion.Current,
            Validated: true,
            Renderer: "test"));
        var renderCount = coordinator.RequestedVersions.Count;

        for (var index = 0; index < 50; index++)
        {
            var recipe = new WMColorRecipe { Name = "draft" };
            recipe.Grade.Exposure = index / 10f;
            await controller.UpdateColorDraftAsync(recipe);
        }
        await WaitUntilAsync(() =>
            controller.State.PreviewPresentation.ColorProgram?.ProgramFingerprint == "program-49");

        Assert.Equal(renderCount, coordinator.RequestedVersions.Count);
        Assert.Equal(50, compiler.Count);
        Assert.Equal("program-49", controller.State.PreviewPresentation.ColorProgram?.ProgramFingerprint);
        Assert.False(controller.State.PreviewPresentation.IsSettled);
    }

    [Fact]
    public async Task WebGlMediaSwitch_KeepsDraftAndLoadsTheSelectedMediaBase()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var compiler = new ImmediateColorPipelineCompiler();
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(SessionWithTwoMedia("gpu-media-switch") with
                { Mode = WMWorkspaceMode.ColorGrade }),
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!,
            colorPipelineCompiler: compiler);
        Assert.True(await controller.OpenAsync("gpu-media-switch"));
        await controller.SetColorPreviewCapabilityAsync(new WMColorPreviewCapability(
            true,
            Max3DTextureSize: 4096,
            PipelineVersion: WMColorPipelineVersion.Current,
            Validated: true,
            Renderer: "test"));
        var draft = Clone(controller.State.ColorGradeTool.Draft);
        draft.Grade.Exposure = 1.25f;
        await controller.UpdateColorDraftAsync(draft);
        await WaitUntilAsync(() =>
            controller.State.PreviewPresentation.ColorProgram is not null);
        var firstBaseFingerprint = controller.State.PreviewPresentation.BaseFingerprint;
        var firstBaseUrl = controller.State.PreviewPresentation.BaseUrl;

        await controller.SelectMediaAsync("media-2");
        await WaitUntilAsync(() =>
            controller.State.CurrentMediaId == "media-2"
            && controller.State.Activity == WMWorkspaceActivity.PreviewReady
            && controller.State.PreviewPresentation.BaseFingerprint != firstBaseFingerprint);

        Assert.Equal(1.25f, controller.State.ColorGradeTool.Draft.Grade.Exposure);
        Assert.NotEqual(firstBaseFingerprint, controller.State.PreviewPresentation.BaseFingerprint);
        Assert.NotEqual(firstBaseUrl, controller.State.PreviewPresentation.BaseUrl);
        Assert.Equal(string.Empty, controller.State.Message);
        Assert.False(controller.State.CanCancel);
    }

    [Fact]
    public async Task MultiFrameAndCollageDrafts_CancelRestoreCommittedConfigurationWithoutRendering()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var initial = SessionWithTwoMedia("configuration-cancel") with
        {
            Mode = WMWorkspaceMode.MultiFrame,
            MultiFrameConfiguration = new WMMultiFrameDraft(
                WMStackMode.StarTrail,
                new Dictionary<string, WMFrameRole?>
                {
                    ["media-1"] = WMFrameRole.Light,
                    ["media-2"] = WMFrameRole.Dark
                },
                NormalizeExposure: true,
                RepairHotPixels: true,
                AutoCrop: false),
            CollageConfiguration = new WMCollageDraft(
                ["media-2", "media-1"],
                WMCollageDirection.Vertical)
        };
        var store = new RecordingSessionStore(initial);
        var coordinator = new StrictlyIncreasingRenderCoordinator(previewPath);
        var controller = new WMWorkspaceController(
            store,
            coordinator,
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync(initial.Id));
        var renderCount = coordinator.RequestedVersions.Count;

        await controller.UpdateMultiFrameDraftAsync(
            controller.State.MultiFrameTool.Draft with { NormalizeExposure = false });
        Assert.True(controller.State.HasTransientEdits);
        Assert.Equal(WMWorkspaceMode.MultiFrame, controller.State.TransientEditMode);
        Assert.False(controller.State.MultiFrameTool.Draft.NormalizeExposure);

        await controller.DiscardTransientEditsAsync();

        Assert.True(controller.State.MultiFrameTool.Draft.NormalizeExposure);
        Assert.False(controller.State.HasTransientEdits);
        Assert.Null(controller.State.TransientEditMode);
        Assert.Equal(renderCount, coordinator.RequestedVersions.Count);
        Assert.Null(store.Saved);

        await controller.SetModeAsync(WMWorkspaceMode.Collage);
        await controller.UpdateCollageDraftAsync(
            new WMCollageDraft(["media-1"], WMCollageDirection.Horizontal));
        Assert.True(controller.State.HasTransientEdits);
        Assert.Equal(WMWorkspaceMode.Collage, controller.State.TransientEditMode);

        await controller.DiscardTransientEditsAsync();

        Assert.Equal(["media-2", "media-1"], controller.State.CollageTool.Draft.OrderedMediaIds);
        Assert.Equal(WMCollageDirection.Vertical, controller.State.CollageTool.Draft.Direction);
        Assert.False(controller.State.HasTransientEdits);
        Assert.Equal(renderCount, coordinator.RequestedVersions.Count);
    }

    [Fact]
    public async Task ApplyingMultiFrameAndCollageDrafts_PersistsConfigurationWithoutHistoryOrRendering()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var initial = SessionWithTwoMedia("configuration-apply") with
        {
            Mode = WMWorkspaceMode.MultiFrame
        };
        var store = new MultiSessionStore(initial);
        var coordinator = new StrictlyIncreasingRenderCoordinator(previewPath);
        var controller = new WMWorkspaceController(
            store,
            coordinator,
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync(initial.Id));
        var renderCount = coordinator.RequestedVersions.Count;

        var multiFrameDraft = new WMMultiFrameDraft(
            WMStackMode.StaticSky,
            new Dictionary<string, WMFrameRole?>
            {
                ["media-1"] = WMFrameRole.Light,
                ["media-2"] = WMFrameRole.Light
            },
            NormalizeExposure: false,
            RepairHotPixels: false,
            AutoCrop: true);
        await controller.UpdateMultiFrameDraftAsync(multiFrameDraft);
        await controller.CommitMultiFrameDraftAsync();
        await controller.SetModeAsync(WMWorkspaceMode.Collage);
        var collageDraft = new WMCollageDraft(
            ["media-2", "media-1"],
            WMCollageDirection.Vertical);
        await controller.UpdateCollageDraftAsync(collageDraft);
        await controller.CommitCollageDraftAsync();

        Assert.False(controller.State.HasTransientEdits);
        Assert.Null(controller.State.TransientEditMode);
        Assert.False(controller.State.CanUndo);
        Assert.Empty(controller.State.History);
        Assert.Equal(renderCount, coordinator.RequestedVersions.Count);
        await controller.CloseAsync();

        var persisted = (await store.OpenAsync(initial.Id)).Session!;
        var reopened = new WMWorkspaceController(
            new RecordingSessionStore(persisted),
            new StrictlyIncreasingRenderCoordinator(previewPath),
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await reopened.OpenAsync(initial.Id));

        Assert.Equal(WMStackMode.StaticSky, reopened.State.MultiFrameTool.Draft.Mode);
        Assert.False(reopened.State.MultiFrameTool.Draft.NormalizeExposure);
        Assert.True(reopened.State.MultiFrameTool.Draft.AutoCrop);
        Assert.Equal(["media-2", "media-1"], reopened.State.CollageTool.Draft.OrderedMediaIds);
        Assert.Equal(WMCollageDirection.Vertical, reopened.State.CollageTool.Draft.Direction);
        Assert.False(reopened.State.CanUndo);
        Assert.Empty(reopened.State.History);
        await reopened.CloseAsync();
    }

    [Fact]
    public async Task CropDraft_IsCurrentOnly_RenderFreeUntilCommit_AndUndoable()
    {
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var initial = SessionWithTwoMedia("crop-controller");
        initial = initial with
        {
            Media = initial.Media.Select(item => item with
            {
                Artifact = item.Artifact with { Width = 100, Height = 100 }
            }).ToArray()
        };
        var store = new RecordingSessionStore(initial);
        var coordinator = new StrictlyIncreasingRenderCoordinator(previewPath);
        var controller = new WMWorkspaceController(
            store,
            coordinator,
            new NoopObjectUrlRegistry(),
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync("crop-controller"));
        var rendersAfterOpen = coordinator.RequestedVersions.Count;

        await controller.BeginCropEditAsync();
        for (var index = 0; index < 12; index++)
        {
            await controller.UpdateCropDraftAsync(new WMCropSettings
            {
                CenterX = .5 + index * .001,
                CenterY = .5,
                VisibleWidth = .8,
                VisibleHeight = .8,
                AspectPreset = WMCropAspectPreset.Free
            });
        }
        await controller.SelectMediaAsync("media-2");

        Assert.Equal("media-1", controller.State.CurrentMediaId);
        Assert.Equal(rendersAfterOpen, coordinator.RequestedVersions.Count);
        Assert.True(controller.State.HasTransientEdits);
        Assert.Equal(WMWorkspaceMode.Crop, controller.State.TransientEditMode);

        await controller.CommitCropDraftAsync();

        Assert.Equal(rendersAfterOpen + 1, coordinator.RequestedVersions.Count);
        var transaction = Assert.Single(store.Saved!.Transactions);
        Assert.Equal("裁切图片", transaction.Label);
        Assert.Equal(["media-1"], Assert.Single(transaction.Assignments).MediaIds);
        Assert.True(store.Saved.CropSettingsByMediaId.ContainsKey("media-1"));
        Assert.False(store.Saved.CropSettingsByMediaId.ContainsKey("media-2"));

        var rendersAfterCommit = coordinator.RequestedVersions.Count;
        await controller.BeginCropEditAsync();
        await controller.CommitCropDraftAsync();
        Assert.Single(store.Saved.Transactions);
        Assert.Equal(rendersAfterCommit, coordinator.RequestedVersions.Count);

        await controller.BeginCropEditAsync();
        await controller.UpdateCropDraftAsync(WMCropSettings.Identity);
        await controller.CommitCropDraftAsync();
        Assert.Equal(2, store.Saved.Transactions.Count);
        Assert.True(WMCropPlanner.IsIdentity(store.Saved.CropSettingsByMediaId["media-1"]));

        await controller.UndoAsync();
        Assert.False(WMCropPlanner.IsIdentity(controller.State.CropTool.Committed));
        await controller.RedoAsync();
        Assert.True(WMCropPlanner.IsIdentity(controller.State.CropTool.Committed));

        // Changing only identity preset metadata must not add history or render.
        var rendersAfterReset = coordinator.RequestedVersions.Count;
        await controller.BeginCropEditAsync();
        await controller.UpdateCropDraftAsync(WMCropSettings.Identity with
        {
            AspectPreset = WMCropAspectPreset.Free
        });
        await controller.CommitCropDraftAsync();
        Assert.Equal(2, store.Saved.Transactions.Count);
        Assert.Equal(rendersAfterReset, coordinator.RequestedVersions.Count);
    }

    [Fact]
    public async Task CropThenTemplatePreview_KeepsPublishedImageUrlAliveForDesktopEditor()
    {
        const string templateId = "crop-template-url-regression";
        CreateTemplate(templateId);
        await File.WriteAllBytesAsync(previewPath, [1, 2, 3]);
        var initial = Session("crop-template-url", "crop-template-media");
        initial = initial with
        {
            Media = initial.Media.Select(item => item with
            {
                Artifact = item.Artifact with { Width = 100, Height = 100 }
            }).ToArray()
        };
        var urls = new TrackingOwnerObjectUrlRegistry();
        var controller = new WMWorkspaceController(
            new RecordingSessionStore(initial),
            new StrictlyIncreasingRenderCoordinator(previewPath, includeTemplateLayout: true),
            urls,
            CreatePreviewService(),
            null!,
            null!);
        Assert.True(await controller.OpenAsync(initial.Id));

        await controller.BeginCropEditAsync();
        await controller.UpdateCropDraftAsync(new WMCropSettings
        {
            CenterX = .55,
            CenterY = .5,
            VisibleWidth = .8,
            VisibleHeight = .8,
            AspectPreset = WMCropAspectPreset.Free
        });
        await controller.CommitCropDraftAsync();
        var cropUrl = controller.State.PreviewUrl;

        await controller.PreviewTemplateAsync(
            new WMWorkspaceTemplateEdit(templateId, null),
            WMApplyScope.Current);

        Assert.NotNull(controller.State.TemplateLayout);
        Assert.NotEqual(cropUrl, controller.State.PreviewUrl);
        Assert.True(urls.IsActive(controller.State.PreviewUrl));
        Assert.Equal(2, urls.ActiveOwnerCountByPrefix("workspace:preview:"));

        await controller.CloseAsync();
        Assert.Equal(0, urls.ActiveLeaseCount);
    }

    private static WMWorkspaceController CreateController(RecordingSessionStore store)
    {
        var profiles = new TestExecutionProfileProvider();
        var previewService = CreatePreviewService(profiles);
        var scheduler = new WMProcessingScheduler();
        var colorProcessor = new WMColorGradeOperationProcessor(scheduler);
        var templateRenderer = new WMTemplateRenderer(new WatermarkHelper());
        var exportService = new WMFullResolutionRenderService(
            new WMFastJpegExportService(templateRenderer, colorProcessor, scheduler),
            profiles);
        return new WMWorkspaceController(
            store,
            new NoopRenderCoordinator(),
            new NoopObjectUrlRegistry(),
            previewService,
            exportService,
            null!);
    }

    private static WMWorkspacePreviewService CreatePreviewService(
        IWMExecutionProfileProvider? profiles = null)
    {
        var scheduler = new WMProcessingScheduler();
        var templateRenderer = new WMTemplateRenderer(new WatermarkHelper());
        return new WMWorkspacePreviewService(
            templateRenderer,
            new WMColorGradeOperationProcessor(scheduler),
            scheduler,
            profiles ?? new TestExecutionProfileProvider(),
            new WMWorkspacePerformanceCounters());
    }

    private WMWorkspaceSession Session(string id, string mediaId) => new()
    {
        Id = id,
        CurrentMediaId = mediaId,
        Media =
        [
            new WMWorkspaceMedia
            {
                Id = mediaId,
                DisplayName = $"{mediaId}.jpg",
                OriginalReference = previewPath,
                Artifact = new WMImageArtifact
                {
                    Id = mediaId,
                    FilePath = previewPath,
                    PreviewPath = previewPath,
                    ContentHash = mediaId,
                    Width = 10,
                    Height = 10
                }
            }
        ]
    };

    private WMWorkspaceSession SessionWithTwoMedia(string id)
    {
        var first = Session(id, "media-1");
        var second = first.Media[0] with
        {
            Id = "media-2",
            DisplayName = "media-2.jpg",
            Artifact = first.Media[0].Artifact with { Id = "artifact-2", ContentHash = "media-2" }
        };
        return first with
        {
            Media = [first.Media[0], second],
            SelectedMediaIds = ["media-1", "media-2"]
        };
    }

    private static WMWorkspaceSession SessionForPaths(string id, string firstPath, string secondPath)
    {
        WMWorkspaceMedia Media(string mediaId, string artifactId, string path) => new()
        {
            Id = mediaId,
            DisplayName = $"{mediaId}.png",
            OriginalReference = path,
            Artifact = new WMImageArtifact
            {
                Id = artifactId,
                FilePath = path,
                PreviewPath = path,
                ContentHash = artifactId,
                Width = 32,
                Height = 24
            }
        };
        return new WMWorkspaceSession
        {
            Id = id,
            Media = [Media("media-1", "artifact-1", firstPath), Media("media-2", "artifact-2", secondPath)],
            SelectedMediaIds = ["media-1", "media-2"],
            CurrentMediaId = "media-1"
        };
    }

    private static void WriteImage(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(32, 24);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    public void Dispose()
    {
        try { File.Delete(previewPath); } catch { }
        foreach (var directory in templateDirectories)
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { }
    }

    private void CreateTemplate(string id)
    {
        var directory = Path.Combine(Global.AppPath.TemplatesFolder, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "config.json"),
            Global.CanvasSerialize(new WMCanvas { ID = id, Name = id }));
        templateDirectories.Add(directory);
    }

    private static WMColorRecipe Clone(WMColorRecipe recipe) =>
        System.Text.Json.JsonSerializer.Deserialize<WMColorRecipe>(
            System.Text.Json.JsonSerializer.Serialize(recipe))!;

    private sealed class RecordingSessionStore(WMWorkspaceSession opened) : IWMWorkspaceSessionStore
    {
        public WMWorkspaceSession? Saved { get; private set; }

        public IDisposable AcquireLease(string sessionId) => NoopLease.Instance;
        public string GetSessionDirectory(string sessionId) => Path.GetTempPath();

        public Task<string> CreateAsync(WMWorkspaceCreateRequest request, CancellationToken token = default) =>
            throw new NotSupportedException();
        public Task<string> CreateAsync(
            WMWorkspaceMode mode,
            IReadOnlyList<IWMPhotoImportSource> sources,
            string? templateId = null,
            CancellationToken token = default) => throw new NotSupportedException();

        public Task<WMWorkspaceOpenResult> OpenAsync(string sessionId, CancellationToken token = default) =>
            Task.FromResult(WMWorkspaceOpenResult.Opened(opened));
        public Task<WMWorkspaceOpenResult> RecoverAsync(
            string sessionId,
            WMWorkspaceRecoveryAction action,
            IReadOnlyList<string> affectedIds,
            CancellationToken token = default) => OpenAsync(sessionId, token);

        public Task SaveAsync(WMWorkspaceSession session, CancellationToken token = default)
        {
            Saved = session;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string sessionId) => Task.CompletedTask;
        public Task<IReadOnlyList<WMWorkspaceSession>> ListRecentAsync(int take = 5, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<WMWorkspaceSession>>([]);
        public Task CleanupExpiredAsync(CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class MultiSessionStore(params WMWorkspaceSession[] initial) : IWMWorkspaceSessionStore
    {
        private readonly Dictionary<string, WMWorkspaceSession> sessions =
            initial.ToDictionary(session => session.Id, StringComparer.Ordinal);

        public IDisposable AcquireLease(string sessionId) => NoopLease.Instance;
        public string GetSessionDirectory(string sessionId) => Path.GetTempPath();

        public Task<string> CreateAsync(WMWorkspaceCreateRequest request, CancellationToken token = default) =>
            throw new NotSupportedException();
        public Task<string> CreateAsync(
            WMWorkspaceMode mode,
            IReadOnlyList<IWMPhotoImportSource> sources,
            string? templateId = null,
            CancellationToken token = default) => throw new NotSupportedException();

        public Task<WMWorkspaceOpenResult> OpenAsync(string sessionId, CancellationToken token = default)
        {
            var value = sessions.GetValueOrDefault(sessionId);
            return Task.FromResult(value is null
                ? new WMWorkspaceOpenResult(WMWorkspaceOpenStatus.Missing, null, [])
                : WMWorkspaceOpenResult.Opened(value));
        }
        public Task<WMWorkspaceOpenResult> RecoverAsync(
            string sessionId,
            WMWorkspaceRecoveryAction action,
            IReadOnlyList<string> affectedIds,
            CancellationToken token = default) => OpenAsync(sessionId, token);

        public Task SaveAsync(WMWorkspaceSession session, CancellationToken token = default)
        {
            sessions[session.Id] = session;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string sessionId)
        {
            sessions.Remove(sessionId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WMWorkspaceSession>> ListRecentAsync(
            int take = 5,
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<WMWorkspaceSession>>(sessions.Values.Take(take).ToArray());

        public Task CleanupExpiredAsync(CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class StrictlyIncreasingRenderCoordinator(
        string previewPath,
        bool includeTemplateLayout = false)
        : IWMWorkspaceRenderCoordinator
    {
        private long currentVersion;
        private WMWorkspacePreview? current;

        public event Action<WMWorkspacePreview>? PreviewPublished;
        public List<long> RequestedVersions { get; } = [];

        public WMWorkspacePreviewTicket QueuePreview(
            WMWorkspaceRenderRequest request,
            CancellationToken token = default)
        {
            RequestedVersions.Add(request.Version);
            if (request.Version <= currentVersion)
                throw new ArgumentOutOfRangeException(nameof(request), "预览版本必须递增。");
            currentVersion = request.Version;
            current = new WMWorkspacePreview(
                request.Version,
                request.Fingerprint,
                previewPath,
                "image/jpeg",
                10,
                10,
                TemplateLayout: includeTemplateLayout
                    ? new WMTemplateLayoutSnapshot(
                        10,
                        10,
                        new WMDesignViewport(0, 0, 10, 10),
                        [])
                    : null);
            PreviewPublished?.Invoke(current);
            var completion = Task.FromResult(current);
            return new WMWorkspacePreviewTicket(
                request.SessionId, request.Epoch, request.Version, request.Fingerprint, completion);
        }

        public Task<WMWorkspacePreview> FlushAsync(
            WMWorkspacePreviewTicket ticket,
            CancellationToken token = default) => ticket.Completion.WaitAsync(token);

        public void CancelPreview() { }
    }

    private sealed class BlockingSecondRenderCoordinator(string previewPath)
        : IWMWorkspaceRenderCoordinator
    {
        private readonly TaskCompletionSource<bool> secondPreviewQueued =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<WMWorkspacePreview> secondPreviewCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WMWorkspacePreview? pendingSecondPreview;
        private int requestCount;

        public event Action<WMWorkspacePreview>? PreviewPublished;
        public Task SecondPreviewQueued => secondPreviewQueued.Task;

        public WMWorkspacePreviewTicket QueuePreview(
            WMWorkspaceRenderRequest request,
            CancellationToken token = default)
        {
            var index = Interlocked.Increment(ref requestCount);
            var preview = new WMWorkspacePreview(
                request.Version,
                request.Fingerprint,
                previewPath,
                "image/jpeg",
                index == 1 ? 10 : 20,
                10,
                TemplateLayout: new WMTemplateLayoutSnapshot(
                    index == 1 ? 10 : 20,
                    10,
                    new WMDesignViewport(0, 0, index == 1 ? 10 : 20, 10),
                    []));
            Task<WMWorkspacePreview> completion;
            if (index == 2)
            {
                pendingSecondPreview = preview;
                secondPreviewQueued.TrySetResult(true);
                completion = secondPreviewCompletion.Task;
            }
            else
            {
                PreviewPublished?.Invoke(preview);
                completion = Task.FromResult(preview);
            }
            return new WMWorkspacePreviewTicket(
                request.SessionId,
                request.Epoch,
                request.Version,
                request.Fingerprint,
                completion);
        }

        public Task<WMWorkspacePreview> FlushAsync(
            WMWorkspacePreviewTicket ticket,
            CancellationToken token = default) => ticket.Completion.WaitAsync(token);

        public void ReleaseSecondPreview()
        {
            var preview = pendingSecondPreview
                          ?? throw new InvalidOperationException("第二次预览尚未排队。");
            PreviewPublished?.Invoke(preview);
            secondPreviewCompletion.TrySetResult(preview);
        }

        public void CancelPreview() { }
    }

    private sealed class RecordingRenderPlanCompiler : IWMRenderPlanCompiler
    {
        private readonly WMRenderPlanCompiler inner = new();

        public List<WMCompiledRenderPlan> Plans { get; } = [];

        public async Task<WMCompiledRenderPlan> CompileAsync(
            WMWorkspaceSession session,
            string mediaId,
            WMRenderTarget target,
            CancellationToken token)
        {
            var plan = await inner.CompileAsync(session, mediaId, target, token);
            Plans.Add(plan);
            return plan;
        }
    }

    private sealed class NoopRenderCoordinator : IWMWorkspaceRenderCoordinator
    {
        public event Action<WMWorkspacePreview>? PreviewPublished
        {
            add { }
            remove { }
        }
        public WMWorkspacePreviewTicket QueuePreview(WMWorkspaceRenderRequest request, CancellationToken token = default) =>
            throw new InvalidOperationException("The test session has no media and must not queue a preview.");
        public Task<WMWorkspacePreview> FlushAsync(WMWorkspacePreviewTicket ticket, CancellationToken token = default) =>
            throw new NotSupportedException();
        public void CancelPreview() { }
    }

    private sealed class RecordingDerivedProcessor(string outputPath) : IWMDerivedMediaProcessor
    {
        public string ArtifactId { get; } = "derived-artifact";

        public Task<WMDerivedMediaOutput> ExecuteAsync(
            WMDerivedMediaRequest request,
            IReadOnlyList<WMImageArtifact> inputs,
            string sessionDirectory,
            CancellationToken cancellationToken = default)
        {
            var operation = WMImageOperation.Create(
                WMImageOperationKind.Collage,
                inputs.Select(item => item.Id),
                [ArtifactId],
                request.Collage);
            var artifact = new WMImageArtifact
            {
                Id = ArtifactId,
                FilePath = outputPath,
                PreviewPath = outputPath,
                ParentArtifactIds = inputs.Select(item => item.Id).ToArray(),
                SourceOperation = WMImageOperationKind.Collage,
                OperationId = operation.Id
            };
            return Task.FromResult(new WMDerivedMediaOutput(artifact, operation, "拼图.png"));
        }
    }

    private sealed class ImmediateColorPipelineCompiler : IWMColorPipelineCompiler
    {
        private int count;
        public int Count => count;

        public Task<WMColorPipelineProgram> CompileAsync(
            WMImageArtifact target,
            WMColorRecipe recipe,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var current = Interlocked.Increment(ref count);
            return Task.FromResult(new WMColorPipelineProgram(
                WMColorPipelineVersion.Current,
                $"program-{current - 1}",
                new WMColorGpuProgram(
                    "#version 300 es\nprecision highp float; out vec4 wm_result; void main(){wm_result=vec4(0.0);}",
                    "test-identity",
                    [],
                    [])));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException("等待控制器状态超时。");
            await Task.Delay(10);
        }
    }

    private sealed class NoopObjectUrlRegistry : IWMObjectUrlRegistry
    {
        private long generation;
        public int ActiveLeaseCount { get; private set; }

        public ValueTask<WMObjectUrlLease?> PublishAsync(
            string ownerKey,
            long ownerVersion,
            Stream content,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            ActiveLeaseCount = 1;
            var next = Interlocked.Increment(ref generation);
            return ValueTask.FromResult<WMObjectUrlLease?>(
                new WMObjectUrlLease(ownerKey, ownerVersion, $"blob:{next}", next));
        }

        public ValueTask ReleaseAsync(WMObjectUrlLease lease)
        {
            ActiveLeaseCount = 0;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            ActiveLeaseCount = 0;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingOriginalObjectUrlRegistry : IWMObjectUrlRegistry
    {
        private readonly object gate = new();
        private readonly Dictionary<string, WMObjectUrlLease> leases = new(StringComparer.Ordinal);
        private long generation;

        public TaskCompletionSource OriginalPublishStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowOriginalPublish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActiveLeaseCount
        {
            get { lock (gate) return leases.Count; }
        }

        public async ValueTask<WMObjectUrlLease?> PublishAsync(
            string ownerKey,
            long ownerVersion,
            Stream content,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            if (ownerKey == "workspace:original")
            {
                OriginalPublishStarted.SetResult();
                // Deliberately do not observe cancellation: CloseAsync must own
                // and await every publication, not merely request cancellation.
                await AllowOriginalPublish.Task;
            }

            var next = Interlocked.Increment(ref generation);
            var lease = new WMObjectUrlLease(ownerKey, ownerVersion, $"blob:{next}", next);
            lock (gate) leases[ownerKey] = lease;
            return lease;
        }

        public ValueTask ReleaseAsync(WMObjectUrlLease lease)
        {
            lock (gate)
            {
                if (leases.TryGetValue(lease.OwnerKey, out var current) && current == lease)
                    leases.Remove(lease.OwnerKey);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            lock (gate) leases.Clear();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingOwnerObjectUrlRegistry : IWMObjectUrlRegistry
    {
        private readonly object gate = new();
        private readonly Dictionary<string, WMObjectUrlLease> leases = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> publishCounts = new(StringComparer.Ordinal);
        private long generation;

        public int ActiveLeaseCount
        {
            get { lock (gate) return leases.Count; }
        }

        public int PublishCount(string ownerKey)
        {
            lock (gate) return publishCounts.GetValueOrDefault(ownerKey);
        }

        public int PublishCountByPrefix(string ownerPrefix)
        {
            lock (gate)
                return publishCounts
                    .Where(pair => pair.Key.StartsWith(ownerPrefix, StringComparison.Ordinal))
                    .Sum(pair => pair.Value);
        }

        public int ActiveOwnerCountByPrefix(string ownerPrefix)
        {
            lock (gate)
                return leases.Keys.Count(key => key.StartsWith(ownerPrefix, StringComparison.Ordinal));
        }

        public bool IsActive(string? url)
        {
            lock (gate)
                return url is not null && leases.Values.Any(lease => lease.Url == url);
        }

        public ValueTask<WMObjectUrlLease?> PublishAsync(
            string ownerKey,
            long ownerVersion,
            Stream content,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = Interlocked.Increment(ref generation);
            var lease = new WMObjectUrlLease(ownerKey, ownerVersion, $"blob:{next}", next);
            lock (gate)
            {
                leases[ownerKey] = lease;
                publishCounts[ownerKey] = publishCounts.GetValueOrDefault(ownerKey) + 1;
            }
            return ValueTask.FromResult<WMObjectUrlLease?>(lease);
        }

        public ValueTask ReleaseAsync(WMObjectUrlLease lease)
        {
            lock (gate)
            {
                if (leases.TryGetValue(lease.OwnerKey, out var current) && current == lease)
                    leases.Remove(lease.OwnerKey);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            lock (gate) leases.Clear();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingSecondExportSink : IWMExportSink
    {
        public Task<string> SaveAsync(
            string renderedPath,
            string suggestedFileName,
            WMExportFormat format,
            WMExportDestinationKind destination,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (suggestedFileName.StartsWith("media-2", StringComparison.Ordinal))
                throw new IOException("模拟保存失败");
            return Task.FromResult($"saved:{suggestedFileName}");
        }
    }

    private sealed class TrackingArtifactCache : IWMArtifactCache
    {
        private int activeLeaseCount;
        public int AcquireCount { get; private set; }
        public int ActiveLeaseCount => Volatile.Read(ref activeLeaseCount);

        public Task<WMArtifactCacheEntry?> TryGetAsync(
            string sessionDirectory,
            string fingerprint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<WMArtifactCacheEntry?>(null);

        public Task<WMArtifactCacheEntry> CommitAsync(
            string sessionDirectory,
            string fingerprint,
            string filePath,
            long budgetBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IDisposable AcquireLease(string sessionDirectory, string fingerprint)
        {
            AcquireCount++;
            Interlocked.Increment(ref activeLeaseCount);
            return new CallbackLease(() => Interlocked.Decrement(ref activeLeaseCount));
        }

        public Task TrimAsync(
            string sessionDirectory,
            long budgetBytes,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CallbackLease(Action release) : IDisposable
    {
        private Action? callback = release;

        public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
    }

    private sealed class NoopLease : IDisposable
    {
        public static readonly NoopLease Instance = new();
        public void Dispose() { }
    }

    private sealed class TestExecutionProfileProvider : IWMExecutionProfileProvider
    {
        public WMOperationExecutionOptions GetInteractiveProfile() => new()
        {
            MaxConcurrentImages = 1,
            MaxPixelWorkers = 1,
            PreviewMaxEdge = 1600
        };

        public WMImagingCapabilities GetImagingCapabilities() => WMImagingCapabilities.MobileDisabled;
    }

    private sealed class AvailableImagingCapabilityProvider : IWMImagingCapabilityProvider
    {
        public WMImagingCapabilities Current { get; } = WMImagingCapabilities.DesktopManaged;

        public WMImagingCapabilityStatus Probe(
            WMImagingFeature feature,
            long requiredDiskBytes = 0) => new(
                feature,
                true,
                true,
                true,
                long.MaxValue,
                long.MaxValue,
                null);
    }

    private sealed class RecordingStackEngine : IWMImageStackEngine
    {
        public List<WMImageArtifact> Outputs { get; } = [];
        public List<WMMultiFrameStackSettings> Settings { get; } = [];

        public Task<WMOperationResult> ExecuteAsync(
            WMOperationRequest request,
            WMMultiFrameStackSettings settings,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Settings.Add(settings);
            Directory.CreateDirectory(request.WorkingDirectory);
            var outputPath = Path.Combine(request.WorkingDirectory, "stack-output.png");
            WriteImage(outputPath, SKColors.SteelBlue);
            var operation = WMImageOperation.Create(
                WMImageOperationKind.MultiFrameStack,
                request.Inputs.Select(item => item.Id),
                Array.Empty<string>(),
                settings);
            var output = new WMImageArtifact
            {
                Id = "stack-output",
                FilePath = outputPath,
                ContentHash = "stack-output",
                ParentArtifactIds = request.Inputs.Select(item => item.Id).ToArray(),
                SourceOperation = WMImageOperationKind.MultiFrameStack,
                OperationId = operation.Id,
                Width = 32,
                Height = 24
            };
            operation = operation with { OutputArtifactIds = [output.Id] };
            Outputs.Add(output);
            return Task.FromResult(new WMOperationResult([output], operation));
        }
    }

    private sealed class LatestWinsStackEngine : IWMImageStackEngine
    {
        private int callCount;
        private int canceledCount;

        public int CallCount => Volatile.Read(ref callCount);
        public int CanceledCount => Volatile.Read(ref canceledCount);
        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<WMOperationResult> ExecuteAsync(
            WMOperationRequest request,
            WMMultiFrameStackSettings settings,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1)
            {
                FirstStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref canceledCount);
                    throw;
                }
            }

            Directory.CreateDirectory(request.WorkingDirectory);
            var outputPath = Path.Combine(request.WorkingDirectory, $"preview-output-{call}.png");
            WriteImage(outputPath, SKColors.SteelBlue);
            var output = new WMImageArtifact
            {
                Id = $"preview-output-{call}",
                FilePath = outputPath,
                ContentHash = $"preview-output-{call}",
                Width = 32,
                Height = 24
            };
            var operation = WMImageOperation.Create(
                WMImageOperationKind.MultiFrameStack,
                request.Inputs.Select(item => item.Id),
                [output.Id],
                settings);
            return new WMOperationResult([output], operation);
        }
    }
}
