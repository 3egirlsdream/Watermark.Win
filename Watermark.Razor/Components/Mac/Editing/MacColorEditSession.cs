#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac.Editing;

public sealed record MacColorEditBase(WMImageArtifact Artifact, string PreviewSource);

/// <summary>
/// Holds the immutable inputs for one replaceable color-edit round. The round is
/// sealed when the user leaves color mode, changes the selected set, or performs
/// another history operation.
/// </summary>
public sealed class MacColorEditSession
{
    private readonly Dictionary<string, MacColorEditBase> bases = new(StringComparer.Ordinal);

    public bool IsActive => bases.Count > 0;
    public long Version { get; private set; }
    public string? CommittedOperationId { get; private set; }
    public IReadOnlyDictionary<string, MacColorEditBase> Bases => bases;

    public bool Matches(IEnumerable<string> mediaIds)
    {
        var ids = mediaIds.ToHashSet(StringComparer.Ordinal);
        return ids.Count == bases.Count && ids.All(bases.ContainsKey);
    }

    public void Begin(IReadOnlyDictionary<string, MacColorEditBase> items)
    {
        bases.Clear();
        foreach (var pair in items) bases[pair.Key] = pair.Value;
        CommittedOperationId = null;
        Version++;
    }

    public WMImageArtifact GetBaseArtifact(string mediaId) => bases.TryGetValue(mediaId, out var value)
        ? value.Artifact
        : throw new KeyNotFoundException($"媒体 {mediaId} 不属于当前调色会话。");

    public string? GetBasePreview(string mediaId) => bases.GetValueOrDefault(mediaId)?.PreviewSource;

    public void MarkCommitted(string operationId)
    {
        CommittedOperationId = operationId;
        Version++;
    }

    public void Seal()
    {
        bases.Clear();
        CommittedOperationId = null;
        Version++;
    }
}
