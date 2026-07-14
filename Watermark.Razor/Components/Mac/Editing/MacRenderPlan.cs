#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac.Editing;

public sealed record MacRenderPlanStep(WMImageOperation Operation);

public sealed record MacRenderPlan(
    WMImageArtifact BaseArtifact,
    IReadOnlyList<MacRenderPlanStep> Steps,
    WMImageArtifact CurrentArtifact)
{
    public bool RequiresReplay => Steps.Count > 0;
    public bool HasCommittedHighPrecision =>
        CurrentArtifact.HighPrecision is { FilePath.Length: > 0 } highPrecision
        && File.Exists(highPrecision.FilePath);
}
