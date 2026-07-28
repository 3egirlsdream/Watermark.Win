#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public enum WMPosterMobilePanel
{
    None,
    Layers,
    Properties,
    Add,
    TextContent,
    AssetLibrary,
    AssetCrop
}

/// <summary>
/// A deliberately compact mobile slice of the full desktop inspector. Related
/// values stay together because the mobile numeric wheel no longer needs a
/// second horizontal track below every parameter.
/// </summary>
public enum WMTemplateMobileInspectorSection
{
    Canvas,
    CanvasSize,
    CanvasInsetTop,
    CanvasInsetRight,
    CanvasInsetBottom,
    CanvasInsetLeft,
    CanvasInsets,
    CanvasFrame,
    CanvasFramePadding,
    CanvasFrameCorner,
    CanvasFrameShadow,
    CanvasFrameBlur,
    CanvasImage,
    CanvasImageCorner,
    CanvasImageShadow,
    CanvasImageBlur,
    Container,
    ContainerImage,
    ContainerCorner,
    ContainerShadow,
    ContainerBlur,
    TextTypography,
    TextFormat,
    TextBorder,
    Logo,
    LogoSizing,
    Line,
    Size,
    Position,
    MarginTop,
    MarginRight,
    MarginBottom,
    MarginLeft,
    Padding,
    Overflow,
    // Kept for legacy inspectors. V2 mobile tools expose the four sides separately.
    Spacing,
    Flow,
    Transform
}

public sealed record WMPosterContextToolSpec(
    string Id,
    string Label,
    string Icon,
    WMPosterMobilePanel Panel,
    WMMobileEditorSpace Space,
    WMTemplateMobileInspectorSection? InspectorSection = null);

/// <summary>
/// Holds mobile-only presentation state for the template designer. Domain
/// edits and history remain owned by <see cref="WMTemplateEditorState"/>.
/// </summary>
public sealed class WMPosterEditorPresentationState
{
    public WMPosterMobilePanel Panel { get; private set; } = WMPosterMobilePanel.None;
    public WMMobileEditorSpace Space { get; private set; } = WMMobileEditorSpace.Medium;
    public string? ActiveToolId { get; private set; }
    public WMTemplateMobileInspectorSection? InspectorSection { get; private set; }
    public bool IsTextFieldLibraryOpen { get; private set; }
    public bool HasOpenPanel => Panel != WMPosterMobilePanel.None;

    public string SpaceCssClass => Space switch
    {
        WMMobileEditorSpace.Small => "mobile-space-small",
        WMMobileEditorSpace.Large => "mobile-space-large",
        _ => "mobile-space-medium"
    };

    public void Open(WMPosterContextToolSpec tool)
    {
        Panel = tool.Panel;
        Space = tool.Space;
        ActiveToolId = tool.Id;
        InspectorSection = tool.InspectorSection;
        IsTextFieldLibraryOpen = false;
    }

    public void Open(
        WMPosterMobilePanel panel,
        WMMobileEditorSpace? requestedSpace = null)
    {
        Panel = panel;
        IsTextFieldLibraryOpen = false;
        Space = requestedSpace ?? DefaultSpace(panel);
        ActiveToolId = null;
        InspectorSection = null;
    }

    public void OpenRelatedPanel(
        WMPosterMobilePanel panel,
        WMMobileEditorSpace? requestedSpace = null)
    {
        Panel = panel;
        IsTextFieldLibraryOpen = false;
        Space = requestedSpace ?? DefaultSpace(panel);
    }

    public void SetTextFieldLibraryOpen(bool open)
    {
        if (Panel != WMPosterMobilePanel.TextContent)
            return;

        IsTextFieldLibraryOpen = open;
        Space = open ? WMMobileEditorSpace.Large : WMMobileEditorSpace.Medium;
    }

    public bool TryClosePanel()
    {
        if (IsTextFieldLibraryOpen)
        {
            SetTextFieldLibraryOpen(false);
            return true;
        }

        if (Panel is WMPosterMobilePanel.None)
            return false;

        ClosePanel();
        return true;
    }

    public void ClosePanel()
    {
        Panel = WMPosterMobilePanel.None;
        IsTextFieldLibraryOpen = false;
        Space = WMMobileEditorSpace.Medium;
        ActiveToolId = null;
        InspectorSection = null;
    }

    public void Reset() => ClosePanel();

    public static IReadOnlyList<WMPosterContextToolSpec> ToolsFor(IWMControl? selectedControl)
    {
        var tools = selectedControl switch
        {
            WMText text => TextTools(text),
            WMLogo logo => LogoTools(logo),
            WMContainer container => ContainerTools(container),
            WMLine line => LineTools(line),
            _ => CanvasConfigurationTools().ToList()
        };

        tools.Add(new("layers", "图层", "stack", WMPosterMobilePanel.Layers, WMMobileEditorSpace.Large));
        tools.Add(new("add", "添加", "plus", WMPosterMobilePanel.Add, WMMobileEditorSpace.Small));
        return tools;
    }

    /// <summary>
    /// The canonical canvas configuration set. The template application uses
    /// these ids as shortcuts into the very same mobile editor sections.
    /// </summary>
    public static IReadOnlyList<WMPosterContextToolSpec> CanvasConfigurationTools() =>
    [
        Inspector("canvas", "画布", "image-square", WMMobileEditorSpace.Medium, WMTemplateMobileInspectorSection.Canvas),
        Inspector("canvas-size", "尺寸", "corners-out", WMMobileEditorSpace.Medium, WMTemplateMobileInspectorSection.CanvasSize),
        Inspector("canvas-insets", "边距", "frame-corners", WMMobileEditorSpace.Medium, WMTemplateMobileInspectorSection.CanvasInsets),
        Inspector("canvas-frame", "外框", "frame-corners", WMMobileEditorSpace.Large, WMTemplateMobileInspectorSection.CanvasFrame),
        Inspector("canvas-image", "图片效果", "magic-wand", WMMobileEditorSpace.Large, WMTemplateMobileInspectorSection.CanvasImage)
    ];

    private static List<WMPosterContextToolSpec> TextTools(WMText text)
    {
        var tools = new List<WMPosterContextToolSpec>
        {
            new("text-content", "内容", "text-t", WMPosterMobilePanel.TextContent, WMMobileEditorSpace.Medium),
            Inspector("text-typography", "字体", "text-aa", WMMobileEditorSpace.Large, WMTemplateMobileInspectorSection.TextTypography),
            Inspector("text-format", "排版", "text-align-left", WMMobileEditorSpace.Medium, WMTemplateMobileInspectorSection.TextFormat),
            Inspector("text-border", "边框", "bounding-box", WMMobileEditorSpace.Large, WMTemplateMobileInspectorSection.TextBorder)
        };
        AddLayoutTools(tools, text, includeFlow: false);
        return tools;
    }

    private static List<WMPosterContextToolSpec> LogoTools(WMLogo logo)
    {
        var tools = new List<WMPosterContextToolSpec>
        {
            new("replace", "替换", "arrows-clockwise", WMPosterMobilePanel.AssetLibrary, WMMobileEditorSpace.Large),
            Inspector("logo", "图片", "image", WMMobileEditorSpace.Medium, WMTemplateMobileInspectorSection.Logo),
            Inspector("logo-sizing", "缩放", "corners-out", WMMobileEditorSpace.Medium, WMTemplateMobileInspectorSection.LogoSizing)
        };
        AddLayoutTools(tools, logo, includeFlow: false);
        return tools;
    }

    private static List<WMPosterContextToolSpec> ContainerTools(WMContainer container)
    {
        var tools = new List<WMPosterContextToolSpec>
        {
            new("replace-background", "背景", "image-square", WMPosterMobilePanel.AssetLibrary, WMMobileEditorSpace.Large),
            Inspector("container", "容器", "palette", WMMobileEditorSpace.Medium, WMTemplateMobileInspectorSection.Container),
            Inspector("container-image", "图片效果", "magic-wand", WMMobileEditorSpace.Large, WMTemplateMobileInspectorSection.ContainerImage)
        };
        AddLayoutTools(tools, container, includeFlow: true);
        return tools;
    }

    private static List<WMPosterContextToolSpec> LineTools(WMLine line)
    {
        var tools = new List<WMPosterContextToolSpec>
        {
            Inspector("line", "线条", "minus", WMMobileEditorSpace.Medium, WMTemplateMobileInspectorSection.Line)
        };
        AddLayoutTools(tools, line, includeFlow: false);
        return tools;
    }

    private static void AddLayoutTools(
        ICollection<WMPosterContextToolSpec> tools,
        IWMControl control,
        bool includeFlow)
    {
        if (control is not WMLine)
            tools.Add(Inspector("size", "尺寸", "corners-out", WMMobileEditorSpace.Medium, WMTemplateMobileInspectorSection.Size));
        tools.Add(Inspector(
            "position",
            "位置",
            "crosshair",
            control.Style.Position == WMPosition.Absolute ? WMMobileEditorSpace.Large : WMMobileEditorSpace.Medium,
            WMTemplateMobileInspectorSection.Position));
        tools.Add(Inspector("spacing", "间距", "sliders-horizontal", WMMobileEditorSpace.Large, WMTemplateMobileInspectorSection.Spacing));
        if (includeFlow)
            tools.Add(Inspector("flow", "排列", "rows", WMMobileEditorSpace.Large, WMTemplateMobileInspectorSection.Flow));
        if (control.Style.Position == WMPosition.Absolute)
            tools.Add(Inspector("transform", "变换", "arrows-clockwise", WMMobileEditorSpace.Large, WMTemplateMobileInspectorSection.Transform));
    }

    private static WMPosterContextToolSpec Inspector(
        string id,
        string label,
        string icon,
        WMMobileEditorSpace space,
        WMTemplateMobileInspectorSection section) =>
        new(id, label, icon, WMPosterMobilePanel.Properties, space, section);

    private static WMMobileEditorSpace DefaultSpace(WMPosterMobilePanel panel) => panel switch
    {
        WMPosterMobilePanel.Layers or WMPosterMobilePanel.AssetLibrary => WMMobileEditorSpace.Large,
        WMPosterMobilePanel.Add => WMMobileEditorSpace.Small,
        _ => WMMobileEditorSpace.Medium
    };
}
