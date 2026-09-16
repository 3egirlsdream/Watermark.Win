using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Watermark.MacExplorer.Plugin.Ui;

/// <summary>插件窗口的轻量控件工厂，配色沿用宿主 Fluent 主题的默认资源。</summary>
internal static class UiKit
{
    public static TextBlock Text(string text, double size = 13, double opacity = 1, bool wrap = false, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        Opacity = opacity,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
    };

    public static TextBlock Heading(string text, double size = 15) => Text(text, size, bold: true);

    public static Button Primary(string text, Action action) => Button(text, action, "accent");

    public static Button Secondary(string text, Action action) => Button(text, action, null);

    private static Button Button(string text, Action action, string? styleClass)
    {
        var button = new Button
        {
            Content = text,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(14, 6)
        };
        if (styleClass is not null) button.Classes.Add(styleClass);
        button.Click += (_, _) => action();
        return button;
    }

    public static TextBox Input(string placeholder, string? value = null) => new()
    {
        PlaceholderText = placeholder,
        Text = value ?? string.Empty,
        Height = 32,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    public static Border Card(Control child, Thickness? padding = null, double radius = 10) => new()
    {
        Child = child,
        Padding = padding ?? new Thickness(14),
        CornerRadius = new CornerRadius(radius),
        Background = Subtle,
        BorderThickness = new Thickness(1),
        BorderBrush = Outline
    };

    public static Border Divider() => new() { Height = 1, Background = Outline, Margin = new Thickness(0, 4) };

    public static StackPanel Row(double spacing = 8, params Control[] children) => Stack(Orientation.Horizontal, spacing, children);

    public static StackPanel Column(double spacing = 8, params Control[] children) => Stack(Orientation.Vertical, spacing, children);

    private static StackPanel Stack(Orientation orientation, double spacing, Control[] children)
    {
        var panel = new StackPanel { Orientation = orientation, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    /// <summary>缩略图外框；先放占位字母，位图解码完成后由 <see cref="SetThumbnail"/> 替换。</summary>
    public static Border Thumbnail(string fallback, double width, double height)
    {
        var frame = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Background = Outline,
            Child = Placeholder(fallback)
        };
        return frame;
    }

    public static void SetThumbnail(Border frame, Bitmap? bitmap)
    {
        frame.Child = bitmap is null ? Placeholder(string.Empty) : new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
    }

    private static TextBlock Placeholder(string fallback) => new()
    {
        Text = fallback.Length > 0 ? fallback[..1] : "影",
        FontSize = 18,
        Opacity = 0.6,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public static IBrush Subtle => Resolve("SystemControlBackgroundAltHighBrush", Color.FromArgb(18, 128, 128, 128));

    public static IBrush Outline => Resolve("SystemControlForegroundBaseLowBrush", Color.FromArgb(38, 128, 128, 128));

    private static IBrush Resolve(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush) return brush;
        return new SolidColorBrush(fallback);
    }
}

/// <summary>缩略图位图缓存，窗口关闭时统一释放。</summary>
internal sealed class CoverLoader : IDisposable
{
    private readonly Dictionary<string, Bitmap?> cache = [];

    public async Task<Bitmap?> LoadAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (cache.TryGetValue(path, out var cached)) return cached;
        var bitmap = await Task.Run(() =>
        {
            try
            {
                return new Bitmap(path);
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"封面解码失败（{path}）：{ex.Message}");
                return null;
            }
        }).ConfigureAwait(true);
        cache[path] = bitmap;
        return bitmap;
    }

    public void Dispose()
    {
        foreach (var bitmap in cache.Values) bitmap?.Dispose();
        cache.Clear();
    }
}
