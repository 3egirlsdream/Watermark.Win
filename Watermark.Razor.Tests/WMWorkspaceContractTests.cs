using System.Text.Json;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMWorkspaceContractTests
{
    [Fact]
    public void EffectiveOperations_PrefersExplicitAssignments()
    {
        var legacy = Operation("legacy");
        var assigned = Operation("assigned");
        var transaction = new WMWorkspaceTransaction
        {
            Id = "transaction",
            Label = "模板",
            Operations = [legacy],
            Assignments = [new WMWorkspaceOperationAssignment(["media-a"], [assigned])],
            CreatedAtUtc = DateTime.UtcNow
        };

        Assert.Equal([assigned.Id], transaction.EffectiveOperations.Select(item => item.Id));
    }

    [Fact]
    public void AssignmentTargets_RoundTripWithoutInferringFromArtifacts()
    {
        var transaction = new WMWorkspaceTransaction
        {
            Id = "transaction",
            Label = "批量调色",
            Assignments = [new WMWorkspaceOperationAssignment(["media-a", "media-c"], [Operation("grade")])],
            CreatedAtUtc = DateTime.UtcNow
        };

        var restored = JsonSerializer.Deserialize<WMWorkspaceTransaction>(
            JsonSerializer.Serialize(transaction))!;

        Assert.Equal(["media-a", "media-c"], Assert.Single(restored.Assignments).MediaIds);
    }

    [Fact]
    public void RolloutMasterSwitch_GatesEveryHeavyFeature()
    {
        var options = new WMImagingRolloutOptions(false, true, true, true, true, true, false);

        Assert.All(Enum.GetValues<WMImagingFeature>(), feature => Assert.False(options.IsEnabled(feature)));
    }

    [Fact]
    public void RolloutOptions_KeepFeaturesIndependent()
    {
        var options = new WMImagingRolloutOptions(true, true, false, true, false, true, false);

        Assert.True(options.IsEnabled(WMImagingFeature.Raw));
        Assert.False(options.IsEnabled(WMImagingFeature.StarTrail));
        Assert.True(options.IsEnabled(WMImagingFeature.MultiFrame));
        Assert.False(options.IsEnabled(WMImagingFeature.Png16));
        Assert.True(options.IsEnabled(WMImagingFeature.Tiff16));
    }

    private static WMImageOperation Operation(string id) =>
        WMImageOperation.Create(
            WMImageOperationKind.ColorGrade,
            ["input"],
            [$"output-{id}"],
            new WMColorRecipe { Name = id });
}
