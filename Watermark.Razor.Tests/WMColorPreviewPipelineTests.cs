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
        var compiler = new WMColorPipelineCompiler(mapper, analysis);
        var recipe = new WMColorRecipe { Name = "manual" };
        recipe.Grade.Exposure = .5f;

        var program = await compiler.CompileAsync(
            new WMImageArtifact { Id = "target", FilePath = "/unused/source.jpg", ContentHash = "source" },
            recipe,
            CancellationToken.None);

        Assert.Equal("identity", program.Look.CacheKey);
        Assert.Equal(2, program.Look.LutSize);
        Assert.Equal(0, mapper.Count);
        Assert.Equal(0, analysis.Count);
        Assert.Equal(.5f, program.Adjustments.Grade[0]);
    }

    [Fact]
    public void Parameters_PreserveCpuGradeOrderAndCurveSampling()
    {
        var settings = new WMColorGradeSettings
        {
            Exposure = 1.25f,
            Contrast = 20,
            Highlights = 30,
            Shadows = -40,
            Whites = 50,
            Blacks = -60,
            Temperature = 70,
            Tint = -80,
            Vibrance = 90,
            Saturation = -10,
            MasterCurve =
            [
                new WMCurvePoint { X = 0, Y = 0 },
                new WMCurvePoint { X = .5f, Y = .75f },
                new WMCurvePoint { X = 1, Y = 1 }
            ]
        };
        settings.Hsl[WMHslBand.Blue] = new WMHslAdjustment
            { Hue = 11, Saturation = 22, Luminance = 33 };

        var result = WMColorPreviewParameters.From(settings);

        Assert.Equal([1.25f, 20, 30, -40, 50, -60, 70, -80, 90, -10], result.Grade);
        Assert.Equal(4096, result.MasterCurve.Length);
        Assert.InRange(result.MasterCurve[2048], .749f, .753f);
        Assert.Equal([11, 22, 33], result.Hsl.Skip((int)WMHslBand.Blue * 3).Take(3));
    }

    [Fact]
    public void Validation_UsesCurrentPipelineAndRejectsVisibleShift()
    {
        var validator = new WMColorPreviewValidator();
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
