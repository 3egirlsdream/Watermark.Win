using System.Text.Json;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMRenderPlanCompilerTests
{
    [Fact]
    public async Task InteractiveBase_OmitsTerminalColor_WhileSettledAndExportShareGraph()
    {
        var session = CreateSession(WMWorkspaceMode.ColorGrade);
        var compiler = new WMRenderPlanCompiler();

        var interactive = await compiler.CompileAsync(
            session, "media", WMRenderTarget.InteractiveBase(), CancellationToken.None);
        var settled = await compiler.CompileAsync(
            session, "media", WMRenderTarget.SettledPreview(), CancellationToken.None);
        var export = await compiler.CompileAsync(
            session,
            "media",
            new WMRenderTarget(WMRenderPurpose.Export, null, WMExportFormat.Jpeg8, 92, true),
            CancellationToken.None);

        Assert.Equal([WMImageOperationKind.Template],
            interactive.Steps.Select(step => step.Operation.Kind));
        Assert.Equal([WMImageOperationKind.Template, WMImageOperationKind.ColorGrade],
            settled.Steps.Select(step => step.Operation.Kind));
        Assert.Equal(settled.GraphFingerprint, export.GraphFingerprint);
    }

    [Fact]
    public async Task UiMode_DoesNotParticipateInGraphFingerprint()
    {
        var compiler = new WMRenderPlanCompiler();
        var session = CreateSession(WMWorkspaceMode.Template);
        var template = await compiler.CompileAsync(
            session,
            "media",
            WMRenderTarget.SettledPreview(),
            CancellationToken.None);
        var collage = await compiler.CompileAsync(
            session with { Mode = WMWorkspaceMode.Collage },
            "media",
            WMRenderTarget.SettledPreview(),
            CancellationToken.None);

        Assert.Equal(template.GraphFingerprint, collage.GraphFingerprint);
        Assert.Equal(
            template.Steps.Select(step => step.Operation.ParametersJson),
            collage.Steps.Select(step => step.Operation.ParametersJson));
    }

    private static WMWorkspaceSession CreateSession(WMWorkspaceMode mode)
    {
        var artifact = new WMImageArtifact
        {
            Id = "artifact",
            FilePath = "/tmp/source.jpg",
            PreviewPath = "/tmp/source-preview.jpg",
            ContentHash = "source-content",
            Width = 1200,
            Height = 800
        };
        var media = new WMWorkspaceMedia
        {
            Id = "media",
            DisplayName = "source.jpg",
            OriginalReference = artifact.FilePath,
            Artifact = artifact
        };
        var canvas = new WMCanvas { ID = "template", Name = "template" };
        var template = WMImageOperation.Create(
            WMImageOperationKind.Template,
            [artifact.Id],
            ["template-output"],
            new WMWorkspaceTemplateSelection(canvas.ID, Global.CanvasSerialize(canvas)));
        var recipe = new WMColorRecipe { Name = "grade" };
        recipe.Grade.Exposure = .75f;
        var color = WMImageOperation.Create(
            WMImageOperationKind.ColorGrade,
            [artifact.Id],
            ["color-output"],
            recipe);
        var transaction = new WMWorkspaceTransaction
        {
            Id = "transaction",
            Label = "edits",
            Assignments = [new WMWorkspaceOperationAssignment([media.Id], [template, color])],
            CreatedAtUtc = DateTime.UnixEpoch
        };
        return new WMWorkspaceSession
        {
            Id = "session",
            Mode = mode,
            Media = [media],
            MediaCatalog = [media],
            ActiveMediaIds = [media.Id],
            CurrentMediaId = media.Id,
            Transactions = [transaction],
            HistoryCursor = 1,
            Operations = [template, color],
            TemplateIdsByMediaId = new Dictionary<string, string?> { [media.Id] = canvas.ID },
            ColorRecipesByMediaId = new Dictionary<string, WMColorRecipe?> { [media.Id] = recipe }
        };
    }
}
