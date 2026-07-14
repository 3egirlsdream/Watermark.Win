using Watermark.Shared.Models;

namespace Watermark.Razor.Components.Mac;

public sealed class MacTemplateLibraryEntry
{
    public required string TemplateId { get; init; }
    public required string FolderPath { get; init; }
    public required string ConfigPath { get; init; }
    public DateTime ConfigLastWriteTimeUtc { get; set; }
    public required WMCanvas Canvas { get; set; }
    public string PreviewSrc { get; set; } = string.Empty;
    public DateTime? PreviewGeneratedAt { get; set; }
    public MacTemplateLoadState LoadState { get; set; } = MacTemplateLoadState.PreviewPending;
    public string ErrorMessage { get; set; } = string.Empty;

    public WMTemplateList ToTemplateList()
    {
        return new WMTemplateList
        {
            ID = TemplateId,
            Canvas = Canvas,
            Src = PreviewSrc
        };
    }
}
