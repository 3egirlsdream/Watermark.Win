#nullable enable

namespace Watermark.Razor.Workspace;

public static class WMTemplatePreviewSource
{
    public static string CreateInlineDataUrl(ReadOnlySpan<byte> imageBytes)
    {
        if (imageBytes.IsEmpty)
            throw new ArgumentException("模板预览图片为空。", nameof(imageBytes));

        return $"data:{DetectMimeType(imageBytes)};base64,{Convert.ToBase64String(imageBytes)}";
    }

    private static string DetectMimeType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3
            && bytes[0] == 0xff
            && bytes[1] == 0xd8
            && bytes[2] == 0xff)
            return "image/jpeg";

        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4e
            && bytes[3] == 0x47
            && bytes[4] == 0x0d
            && bytes[5] == 0x0a
            && bytes[6] == 0x1a
            && bytes[7] == 0x0a)
            return "image/png";

        return "application/octet-stream";
    }
}
