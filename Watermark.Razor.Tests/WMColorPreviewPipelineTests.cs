using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMColorPreviewPipelineTests
{
    [Fact]
    public void RecipeSnapshot_DeepCopiesUserAdjustmentsWithoutJsonRoundTrip()
    {
        var recipe = new WMColorRecipe
        {
            Grade = new WMColorGradeSettings { Exposure = .25f },
            UserAdjustments = new WMColorGradeSettings { Exposure = .75f }
        };

        var snapshot = WMColorRecipeSnapshot.Copy(recipe)!;
        recipe.Grade.Exposure = 2;
        recipe.UserAdjustments.Exposure = 3;

        Assert.Equal(.25f, snapshot.Grade.Exposure);
        Assert.Equal(.75f, snapshot.UserAdjustments!.Exposure);
        Assert.NotSame(snapshot.Grade, snapshot.UserAdjustments);
    }

    [Fact]
    public async Task RecipeWithoutReference_UsesSmallIdentityLook_WithoutAnalysisOrMapping()
    {
        var mapper = new RecordingLookMapper();
        var analysis = new RecordingAnalysisService();
        var engine = RequireEngine();
        using var compiler = new WMColorPipelineCompiler(mapper, analysis, engine);
        var recipe = new WMColorRecipe { Name = "manual" };
        recipe.Grade.Exposure = .5f;

        var program = await compiler.CompileAsync(
            new WMImageArtifact { Id = "target", FilePath = "/unused/source.jpg", ContentHash = "source" },
            recipe,
            CancellationToken.None);

        Assert.Equal(WMColorPipelineVersion.Current, program.PipelineVersion);
        Assert.NotEmpty(program.GpuProgram.FragmentProgram);
        Assert.NotEmpty(program.GpuProgram.ShaderCacheId);
        Assert.Equal(0, mapper.Count);
        Assert.Equal(0, analysis.Count);
    }

    [Fact]
    public async Task DynamicGrade_ReusesStaticProcessorAndShaderTopology()
    {
        var engine = RequireEngine();
        engine.Metrics.Reset();
        using var compiler = new WMColorPipelineCompiler(
            new RecordingLookMapper(), new RecordingAnalysisService(), engine);
        var target = new WMImageArtifact { Id = "target", FilePath = "/unused/source.jpg", ContentHash = "source" };
        var recipe = new WMColorRecipe { Name = "manual" };
        var first = await compiler.CompileAsync(target, recipe, CancellationToken.None);
        recipe.Grade.Exposure = 1.25f;
        var second = await compiler.CompileAsync(target, recipe, CancellationToken.None);
        var metrics = engine.Metrics.Snapshot().Calls;

        Assert.Equal(1, metrics.GetValueOrDefault(WMColorEngineMetricStage.ProcessorCompile));
        Assert.Equal(2, metrics.GetValueOrDefault(WMColorEngineMetricStage.DynamicUpdate));
        Assert.Equal(first.GpuProgram.ShaderCacheId, second.GpuProgram.ShaderCacheId);
        Assert.Same(first.GpuProgram.FragmentProgram, second.GpuProgram.FragmentProgram);
        Assert.NotEqual(first.ProgramFingerprint, second.ProgramFingerprint);
        Assert.True(first.GpuProgram.Textures.SelectMany(texture => texture.Values)
            .SequenceEqual(second.GpuProgram.Textures.SelectMany(texture => texture.Values)));
        Assert.Contains(first.GpuProgram.Uniforms.Zip(second.GpuProgram.Uniforms), pair =>
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)
            && !pair.First.Values.SequenceEqual(pair.Second.Values));
    }

    [Fact]
    public void Validation_UsesCurrentPipelineAndRejectsVisibleShift()
    {
        var validator = new WMColorPreviewValidator(RequireEngine());
        var request = validator.CreateRequest();

        var exact = validator.Evaluate(request, request.ExpectedRgba.ToArray());
        var shiftedBytes = request.ExpectedRgba.ToArray();
        for (var index = 0; index < shiftedBytes.Length; index += 4)
            shiftedBytes[index] = (byte)Math.Min(255, shiftedBytes[index] + 12);
        var shifted = validator.Evaluate(request, shiftedBytes);

        Assert.Equal(WMColorPipelineVersion.Current, request.PipelineVersion);
        Assert.True(exact.Passed);
        Assert.Equal(0, exact.AverageDeltaE);
        Assert.False(shifted.Passed);
        Assert.NotNull(shifted.Reason);
    }

    private static WMOcioColorEngine RequireEngine()
    {
        var engine = new WMOcioColorEngine();
        Assert.True(engine.Capability.IsAvailable, engine.Capability.Error);
        return engine;
    }

    private sealed class RecordingLookMapper : IWMColorLookMapper
    {
        public int Count { get; private set; }

        public WMGeneratedColorLook Map(
            WMColorLookMappingRequest request,
            CancellationToken cancellationToken = default)
        {
            Count++;
            throw new InvalidOperationException("无参考图时不应生成Look。");
        }
    }

    private sealed class RecordingAnalysisService : IWMColorAnalysisService
    {
        public int Count { get; private set; }

        public WMColorReferenceProfile Analyze(
            string imagePath,
            CancellationToken cancellationToken = default)
        {
            Count++;
            throw new InvalidOperationException("无参考图时不应分析目标素材。");
        }

        public WMColorReferenceProfile Analyze(
            SkiaSharp.SKBitmap bitmap,
            string? sourceHash = null,
            CancellationToken cancellationToken = default)
        {
            Count++;
            throw new InvalidOperationException("无参考图时不应分析目标素材。");
        }
    }
}
