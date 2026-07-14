#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac;

public sealed record MacPreparedTemplatePreview(
    string InputArtifactId,
    string Fingerprint,
    WMImageArtifact Artifact,
    WMDesignRenderResult RenderResult);
