using System.Text.Json;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace.Components;

internal static class WMWorkspacePanelMutations
{
    public static WMColorRecipe SetColorValue(WMColorRecipe source, string key, float value)
    {
        var next = Clone(source);
        switch (key)
        {
            case "Exposure": next.Grade.Exposure = value; break;
            case "Contrast": next.Grade.Contrast = value; break;
            case "Highlights": next.Grade.Highlights = value; break;
            case "Shadows": next.Grade.Shadows = value; break;
            case "Whites": next.Grade.Whites = value; break;
            case "Blacks": next.Grade.Blacks = value; break;
            case "Temperature": next.Grade.Temperature = value; break;
            case "Tint": next.Grade.Tint = value; break;
            case "Vibrance": next.Grade.Vibrance = value; break;
            case "Saturation": next.Grade.Saturation = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown color parameter.");
        }
        next.UserAdjustments = next.Grade;
        return next;
    }

    public static WMColorRecipe QuickLook(
        string name,
        float exposure,
        float contrast,
        float temperature,
        float vibrance)
    {
        var next = new WMColorRecipe { Name = name };
        next.Grade.Exposure = exposure;
        next.Grade.Contrast = contrast;
        next.Grade.Temperature = temperature;
        next.Grade.Vibrance = vibrance;
        next.UserAdjustments = next.Grade;
        return next;
    }

    public static WMCanvas SetBorder(WMCanvas source, string side, double value)
    {
        var next = Global.ReadConfig(Global.CanvasSerialize(source));
        value = Math.Clamp(value, 0, 100);
        switch (side)
        {
            case "Top": next.BorderThickness.Top = value; break;
            case "Right": next.BorderThickness.Right = value; break;
            case "Bottom": next.BorderThickness.Bottom = value; break;
            case "Left": next.BorderThickness.Left = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown template border side.");
        }
        return next;
    }

    private static WMColorRecipe Clone(WMColorRecipe value) =>
        JsonSerializer.Deserialize<WMColorRecipe>(JsonSerializer.Serialize(value))!;
}
