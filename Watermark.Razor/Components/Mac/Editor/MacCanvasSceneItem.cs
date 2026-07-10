#nullable enable

namespace Watermark.Razor.Components.Mac.Editor;

public sealed record MacCanvasSceneItem(
    string Id,
    string? ParentId,
    string Type,
    double X,
    double Y,
    double Width,
    double Height,
    double ParentWidth,
    double ParentHeight,
    double OffsetXPercent,
    double OffsetYPercent,
    double ScaleX,
    double ScaleY,
    double Rotation,
    bool Locked,
    bool Visible);

public sealed record MacCanvasInteraction(
    string ControlId,
    string Kind,
    double OffsetXPercent,
    double OffsetYPercent,
    double ScaleX,
    double ScaleY,
    double Rotation);
