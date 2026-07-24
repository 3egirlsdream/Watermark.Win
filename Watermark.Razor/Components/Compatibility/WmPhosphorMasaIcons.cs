#nullable enable

using Masa.Blazor;
using Microsoft.Extensions.DependencyInjection;

namespace Watermark.Razor.Components.Compatibility;

public static class WmPhosphorMasaIcons
{
    private const string Weight = "regular";
    private const double MasaViewBoxScale = 24d / 256d;

    public static IMasaBlazorBuilder AddWatermarkMasaBlazor(
        this IServiceCollection services,
        Action<MasaBlazorOptions>? configure = null,
        ServiceLifetime serviceLifetime = ServiceLifetime.Scoped)
    {
        return services.AddMasaBlazor(options =>
        {
            configure?.Invoke(options);
            options.ConfigureIcons("phosphor", CreateAliases());
        }, serviceLifetime);
    }

    public static IconAliases CreateAliases() => new()
    {
        Complete = Svg("check"),
        Cancel = Svg("x-circle"),
        Close = Svg("x"),
        Delete = Svg("x-circle"),
        Clear = Svg("x-circle"),
        Success = Svg("check-circle"),
        Info = Svg("info"),
        Warning = Svg("warning-circle"),
        Error = Svg("x-circle"),
        Prev = Svg("caret-left"),
        Next = Svg("caret-right"),
        CheckboxOn = Svg("check-square"),
        CheckboxOff = Svg("square"),
        CheckboxIndeterminate = Svg("minus-square"),
        Delimiter = Svg("circle"),
        Sort = Svg("arrow-up"),
        Expand = Svg("caret-down"),
        Menu = Svg("list"),
        Subgroup = Svg("caret-down"),
        Dropdown = Svg("caret-down"),
        RadioOn = Svg("radio-button"),
        RadioOff = Svg("circle"),
        Edit = Svg("pencil"),
        RatingEmpty = Svg("star"),
        RatingFull = Svg("star"),
        RatingHalf = Svg("star-half"),
        Loading = Svg("spinner-gap"),
        First = Svg("caret-line-left"),
        Last = Svg("caret-line-right"),
        Unfold = Svg("caret-up-down"),
        File = Svg("paperclip"),
        Plus = Svg("plus"),
        Minus = Svg("minus"),
        Increase = Svg("caret-up"),
        Decrease = Svg("caret-down"),
        Copy = Svg("copy"),
        GoBack = Svg("arrow-left"),
        Search = Svg("magnifying-glass"),
        FilterOn = Svg("funnel"),
        FilterOff = Svg("funnel-x"),
        Retry = Svg("arrows-clockwise")
    };

    private static SvgPath Svg(string name) => new(
        WmPhosphorIconPaths.Get(name, Weight),
        new Dictionary<string, object>
        {
            ["transform"] = $"scale({MasaViewBoxScale.ToString(System.Globalization.CultureInfo.InvariantCulture)})"
        });
}
