using SkiaSharp;
using Watermark.Razor.Components.Mac.Editing;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacEditingSessionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "watermark-session-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RegisterSource_RecordsExistingImportedPreview()
    {
        using var session = new MacEditingSession(root);
        var sourcePath = CreateImage("preview-source.png", SKColors.Red);
        var previewPath = CreateImage("preview-thumbnail.png", SKColors.Blue);

        var source = session.RegisterSource("a", sourcePath, previewPath: previewPath);

        Assert.Equal(previewPath, source.PreviewPath);
    }

    [Fact]
    public void CommitUndoRedo_ChangesCurrentArtifactAsOneAtomicBatch()
    {
        using var session = new MacEditingSession(root);
        var sourceA = CreateImage("a-source.png", SKColors.Red);
        var sourceB = CreateImage("b-source.png", SKColors.Blue);
        var beforeA = session.RegisterSource("a", sourceA);
        var beforeB = session.RegisterSource("b", sourceB);
        var afterA = CreateArtifact(session, "a-after.png", beforeA, WMImageOperationKind.ColorGrade, SKColors.Orange);
        var afterB = CreateArtifact(session, "b-after.png", beforeB, WMImageOperationKind.ColorGrade, SKColors.Cyan);
        var operation = WMImageOperation.Create(
            WMImageOperationKind.ColorGrade,
            [beforeA.Id, beforeB.Id],
            [afterA.Id, afterB.Id],
            new WMColorRecipe());

        session.Commit(operation, new Dictionary<string, WMImageArtifact> { ["a"] = afterA, ["b"] = afterB });

        Assert.Equal(afterA.Id, session.GetCurrent("a").Id);
        Assert.Equal(afterB.Id, session.GetCurrent("b").Id);
        var undone = session.Undo();
        Assert.Equal(beforeA.Id, session.GetCurrent("a").Id);
        Assert.Equal(beforeB.Id, session.GetCurrent("b").Id);
        Assert.Equal(2, undone.CurrentArtifacts.Count);
        session.Redo();
        Assert.Equal(afterA.Id, session.GetCurrent("a").Id);
        Assert.Equal(afterB.Id, session.GetCurrent("b").Id);
    }

    [Fact]
    public void CommitAfterUndo_RemovesRedoHistoryAndAbandonedCache()
    {
        using var session = new MacEditingSession(root);
        var source = session.RegisterSource("a", CreateImage("source.png", SKColors.Red));
        var first = CreateArtifact(session, "first.png", source, WMImageOperationKind.Template, SKColors.Green);
        session.Commit(CreateOperation(source, first), new Dictionary<string, WMImageArtifact> { ["a"] = first });
        session.Undo();

        var replacement = CreateArtifact(session, "replacement.png", source, WMImageOperationKind.ColorGrade, SKColors.Blue);
        session.Commit(CreateOperation(source, replacement), new Dictionary<string, WMImageArtifact> { ["a"] = replacement });

        Assert.False(session.CanRedo);
        Assert.False(File.Exists(first.FilePath));
        Assert.Equal(replacement.Id, session.GetCurrent("a").Id);
    }

    [Fact]
    public void ReplaceLastColorOperation_KeepsOneUndoStepAndStableBase()
    {
        using var session = new MacEditingSession(root);
        var source = session.RegisterSource("a", CreateImage("replace-source.png", SKColors.Red));
        var first = CreateArtifact(session, "replace-first.png", source, WMImageOperationKind.ColorGrade, SKColors.Green);
        var firstOperation = WMImageOperation.Create(WMImageOperationKind.ColorGrade, [source.Id], [first.Id], new WMColorRecipe());
        first = first with { OperationId = firstOperation.Id };
        session.Commit(firstOperation, new Dictionary<string, WMImageArtifact> { ["a"] = first });

        var second = CreateArtifact(session, "replace-second.png", source, WMImageOperationKind.ColorGrade, SKColors.Blue);
        var secondOperation = WMImageOperation.Create(WMImageOperationKind.ColorGrade, [source.Id], [second.Id], new WMColorRecipe());
        second = second with { OperationId = secondOperation.Id };
        session.ReplaceLast(firstOperation.Id, secondOperation, new Dictionary<string, WMImageArtifact> { ["a"] = second });

        Assert.Single(session.History);
        Assert.Equal(second.Id, session.GetCurrent("a").Id);
        session.Undo();
        Assert.Equal(source.Id, session.GetCurrent("a").Id);
    }

    [Fact]
    public void CreatedMedia_UndoRemovesAndRedoRestoresIt()
    {
        using var session = new MacEditingSession(root);
        var sourceA = session.RegisterSource("a", CreateImage("a.png", SKColors.Black));
        var sourceB = session.RegisterSource("b", CreateImage("b.png", SKColors.White));
        var starTrail = CreateArtifact(session, "trail.png", sourceA, WMImageOperationKind.StarTrail, SKColors.Gray);
        starTrail = starTrail with { ParentArtifactIds = [sourceA.Id, sourceB.Id] };
        var operation = WMImageOperation.Create(WMImageOperationKind.StarTrail, starTrail.ParentArtifactIds, [starTrail.Id], new { });

        session.Commit(operation, new Dictionary<string, WMImageArtifact> { ["trail"] = starTrail }, ["trail"]);
        Assert.Equal(["trail"], session.Undo().RemovedMediaIds);
        Assert.False(session.TryGetCurrent("trail", out _));
        Assert.Equal(["trail"], session.Redo().AddedMediaIds);
        Assert.Equal(starTrail.Id, session.GetCurrent("trail").Id);
    }

    [Fact]
    public void MissingBatchOutput_DoesNotCommitAnyMedia()
    {
        using var session = new MacEditingSession(root);
        var sourceA = session.RegisterSource("a", CreateImage("a.png", SKColors.Red));
        var sourceB = session.RegisterSource("b", CreateImage("b.png", SKColors.Blue));
        var valid = CreateArtifact(session, "valid.png", sourceA, WMImageOperationKind.ColorGrade, SKColors.Green);
        var missing = new WMImageArtifact
        {
            Id = Guid.NewGuid().ToString("N"),
            FilePath = Path.Combine(session.SessionDirectory, "missing.png"),
            ParentArtifactIds = [sourceB.Id],
            SourceOperation = WMImageOperationKind.ColorGrade
        };

        Assert.Throws<FileNotFoundException>(() => session.Commit(
            WMImageOperation.Create(WMImageOperationKind.ColorGrade, [sourceA.Id, sourceB.Id], [valid.Id, missing.Id], new { }),
            new Dictionary<string, WMImageArtifact> { ["a"] = valid, ["b"] = missing }));
        Assert.Equal(sourceA.Id, session.GetCurrent("a").Id);
        Assert.Equal(sourceB.Id, session.GetCurrent("b").Id);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void BuildRenderPlan_UsesFullSizeBaseAndPreservesAppliedOrder()
    {
        using var session = new MacEditingSession(root);
        var source = session.RegisterSource("a", CreateImage("plan-source.png", SKColors.Red));
        var template = CreateArtifact(session, "plan-template.png", source, WMImageOperationKind.Template, SKColors.Green);
        var templateOperation = WMImageOperation.Create(
            WMImageOperationKind.Template, [source.Id], [template.Id], new WMTemplateOperationSettings(new WMCanvas()));
        template = template with { OperationId = templateOperation.Id };
        session.Commit(templateOperation, new Dictionary<string, WMImageArtifact> { ["a"] = template });

        var color = CreateArtifact(session, "plan-color.png", template, WMImageOperationKind.ColorGrade, SKColors.Blue);
        var colorOperation = WMImageOperation.Create(
            WMImageOperationKind.ColorGrade, [template.Id], [color.Id], new WMColorRecipe());
        color = color with { OperationId = colorOperation.Id };
        session.Commit(colorOperation, new Dictionary<string, WMImageArtifact> { ["a"] = color });

        var plan = session.BuildRenderPlan("a");

        Assert.Equal(source.Id, plan.BaseArtifact.Id);
        Assert.Equal(color.Id, plan.CurrentArtifact.Id);
        Assert.Equal(
            [WMImageOperationKind.Template, WMImageOperationKind.ColorGrade],
            plan.Steps.Select(step => step.Operation.Kind));

        session.Undo();
        plan = session.BuildRenderPlan("a");
        Assert.Equal(template.Id, plan.CurrentArtifact.Id);
        Assert.Equal([WMImageOperationKind.Template], plan.Steps.Select(step => step.Operation.Kind));
    }

    [Fact]
    public void BuildRenderPlan_StopsAtFullSizeStarTrailArtifact()
    {
        using var session = new MacEditingSession(root);
        var sourceA = session.RegisterSource("a", CreateImage("star-a.png", SKColors.Black));
        var sourceB = session.RegisterSource("b", CreateImage("star-b.png", SKColors.White));
        var star = CreateArtifact(session, "star-full.png", sourceA, WMImageOperationKind.StarTrail, SKColors.Gray)
            with { ParentArtifactIds = [sourceA.Id, sourceB.Id] };
        var starOperation = WMImageOperation.Create(
            WMImageOperationKind.StarTrail, star.ParentArtifactIds, [star.Id], new { });
        star = star with { OperationId = starOperation.Id };
        session.Commit(starOperation, new Dictionary<string, WMImageArtifact> { ["star"] = star }, ["star"]);

        var color = CreateArtifact(session, "star-color-proxy.png", star, WMImageOperationKind.ColorGrade, SKColors.Blue);
        var colorOperation = WMImageOperation.Create(
            WMImageOperationKind.ColorGrade, [star.Id], [color.Id], new WMColorRecipe());
        color = color with { OperationId = colorOperation.Id };
        session.Commit(colorOperation, new Dictionary<string, WMImageArtifact> { ["star"] = color });

        var plan = session.BuildRenderPlan("star");

        Assert.Equal(star.Id, plan.BaseArtifact.Id);
        Assert.Equal(WMImageOperationKind.StarTrail, plan.BaseArtifact.SourceOperation);
        Assert.Equal([WMImageOperationKind.ColorGrade], plan.Steps.Select(step => step.Operation.Kind));
    }

    private WMImageArtifact CreateArtifact(MacEditingSession session, string fileName, WMImageArtifact parent, WMImageOperationKind kind, SKColor color)
    {
        var path = Path.Combine(session.GetWorkingDirectory(kind), fileName);
        WriteImage(path, color);
        return new WMImageArtifact
        {
            Id = Guid.NewGuid().ToString("N"),
            FilePath = path,
            ParentArtifactIds = [parent.Id],
            SourceOperation = kind,
            Width = 8,
            Height = 8
        };
    }

    private static WMImageOperation CreateOperation(WMImageArtifact before, WMImageArtifact after) =>
        WMImageOperation.Create(after.SourceOperation, [before.Id], [after.Id], new { });

    private string CreateImage(string relativePath, SKColor color)
    {
        var path = Path.Combine(root, relativePath);
        WriteImage(path, color);
        return path;
    }

    private static void WriteImage(string path, SKColor color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(8, 8);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }
}
