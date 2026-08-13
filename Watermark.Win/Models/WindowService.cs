#nullable enable

using Microsoft.JSInterop;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;

namespace Watermark.Win.Models;

public sealed class WindowService : IWindowService
{
    private static bool isMoving;
    private static Point startMouse;
    private static Point startWindow;
    private static Window? movingWindow;

    [JSInvokable]
    public static void StartMove()
    {
        var window = GetActiveWindow();
        if (window is null || window.WindowState == WindowState.Maximized) return;
        movingWindow = window;
        startMouse = GetMousePosition(window);
        startWindow = new Point(window.Left, window.Top);
        isMoving = true;
    }

    [JSInvokable]
    public static void StopMove()
    {
        isMoving = false;
        movingWindow = null;
    }

    [JSInvokable]
    public static void UpdateWindowPos()
    {
        var window = movingWindow;
        if (!isMoving || window is null) return;
        var mouse = GetMousePosition(window);
        window.Left = startWindow.X + mouse.X - startMouse.X;
        window.Top = startWindow.Y + mouse.Y - startMouse.Y;
    }

    public void Minimize()
    {
        if (GetActiveWindow() is { } window) window.WindowState = WindowState.Minimized;
    }

    public void Maximize()
    {
        if (GetActiveWindow() is not { } window) return;
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    public bool IsMaximized() => GetActiveWindow()?.WindowState == WindowState.Maximized;

    public void Close(bool allWindow = false)
    {
        if (allWindow)
        {
            Application.Current?.Shutdown();
            return;
        }
        GetActiveWindow()?.Close();
    }

    private static Window? GetActiveWindow() => Application.Current?.Windows
        .OfType<Window>()
        .FirstOrDefault(window => window.IsActive)
        ?? Application.Current?.MainWindow;

    private static Point GetMousePosition(Window window)
    {
        var screenPoint = System.Windows.Forms.Control.MousePosition;
        var pixels = new Point(screenPoint.X, screenPoint.Y);
        var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice
                        ?? Matrix.Identity;
        return transform.Transform(pixels);
    }
}
