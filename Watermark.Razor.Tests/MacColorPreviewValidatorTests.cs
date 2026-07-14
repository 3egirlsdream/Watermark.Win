using Watermark.Razor.Components.Mac;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacColorPreviewValidatorTests
{
    [Fact]
    public void ValidationRequest_UsesCurrentPipelineAndDeterministicCpuBaseline()
    {
        var validator = new MacColorPreviewValidator();

        var first = validator.CreateRequest();
        var second = validator.CreateRequest();

        Assert.Equal(WMColorPipelineVersion.Current, first.PipelineVersion);
        Assert.Equal(WMColorPipelineVersion.Current, first.Look.PipelineVersion);
        Assert.Equal(first.SourceRgba, second.SourceRgba);
        Assert.Equal(first.ExpectedRgba, second.ExpectedRgba);
        Assert.Equal(first.Width * first.Height * 4, first.ExpectedRgba.Length);
    }

    [Fact]
    public void Evaluate_AcceptsExactResultAndRejectsVisibleColorShift()
    {
        var validator = new MacColorPreviewValidator();
        var request = validator.CreateRequest();

        var exact = validator.Evaluate(request, request.ExpectedRgba.ToArray());
        var shiftedBytes = request.ExpectedRgba.ToArray();
        for (var index = 0; index < shiftedBytes.Length; index += 4)
            shiftedBytes[index] = (byte)Math.Min(255, shiftedBytes[index] + 12);
        var shifted = validator.Evaluate(request, shiftedBytes);

        Assert.True(exact.Passed);
        Assert.Equal(0, exact.AverageDeltaE);
        Assert.False(shifted.Passed);
        Assert.NotNull(shifted.Reason);
    }

    [Fact]
    public void Evaluate_RejectsInvalidPixelBuffer()
    {
        var validator = new MacColorPreviewValidator();
        var request = validator.CreateRequest();

        var result = validator.Evaluate(request, []);

        Assert.False(result.Passed);
        Assert.Contains("预期", result.Reason);
    }
}
