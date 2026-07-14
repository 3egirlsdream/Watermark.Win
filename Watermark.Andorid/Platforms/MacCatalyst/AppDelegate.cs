using Foundation;
using ObjCRuntime;
using System.Runtime.InteropServices;
using UIKit;

namespace Watermark.Andorid
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

    public static class WindowHelper
    {
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern void void_objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern void void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern nint nint_objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern IntPtr IntPtr_objc_msgSend_nint(IntPtr receiver, IntPtr selector, nint arg);

        static IntPtr _cachedNSWindow = IntPtr.Zero;

        static UIWindowScene? GetWindowScene()
        {
            return UIApplication.SharedApplication
                .ConnectedScenes
                .OfType<UIWindowScene>()
                .FirstOrDefault();
        }

        static IntPtr GetNSWindowFromScene()
        {
            // 先尝试缓存
            if (_cachedNSWindow != IntPtr.Zero) return _cachedNSWindow;
            try
            {
                var nsApp = Class.GetHandle("NSApplication");
                var sharedApp = IntPtr_objc_msgSend(nsApp, Selector.GetHandle("sharedApplication"));
                if (sharedApp == IntPtr.Zero) return IntPtr.Zero;
                // 先尝试 keyWindow
                var win = IntPtr_objc_msgSend(sharedApp, Selector.GetHandle("keyWindow"));
                // keyWindow 可能为空（窗口未激活），尝试 mainWindow
                if (win == IntPtr.Zero)
                    win = IntPtr_objc_msgSend(sharedApp, Selector.GetHandle("mainWindow"));
                // 还是空的话，尝试从 windows 数组取第一个
                if (win == IntPtr.Zero)
                {
                    var windows = IntPtr_objc_msgSend(sharedApp, Selector.GetHandle("windows"));
                    if (windows != IntPtr.Zero)
                    {
                        var count = nint_objc_msgSend(windows, Selector.GetHandle("count"));
                        if (count > 0)
                            win = IntPtr_objc_msgSend_nint(windows, Selector.GetHandle("objectAtIndex:"), 0);
                    }
                }
                if (win != IntPtr.Zero) _cachedNSWindow = win;
                return win;
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>
        /// 配置标题栏：保留 macOS 原生标题栏和窗口按钮，只设置桌面端尺寸约束。
        /// 必须在 window.Created 事件之后调用
        /// </summary>
        public static void ConfigureTitleBar()
        {
            var scene = GetWindowScene();
            if (scene == null) return;

            if (scene.SizeRestrictions != null)
            {
                scene.SizeRestrictions.MinimumSize = new CoreGraphics.CGSize(900, 600);
            }
        }

        public static void StartDrag()
        {
            // macOS 原生标题栏负责窗口拖动，这里仅保留接口兼容。
        }

        public static void DragMove()
        {
            // macOS 原生标题栏负责窗口拖动，这里仅保留接口兼容。
        }

        public static void EndDrag()
        {
            // macOS 原生标题栏负责窗口拖动，这里仅保留接口兼容。
        }

        public static void Minimize()
        {
            try
            {
                var nsWindow = GetNSWindowFromScene();
                if (nsWindow == IntPtr.Zero) return;
                void_objc_msgSend_IntPtr(nsWindow, Selector.GetHandle("miniaturize:"), IntPtr.Zero);
            }
            catch { }
        }

        public static void Zoom()
        {
            try
            {
                var nsWindow = GetNSWindowFromScene();
                if (nsWindow == IntPtr.Zero) return;
                void_objc_msgSend_IntPtr(nsWindow, Selector.GetHandle("zoom:"), IntPtr.Zero);
            }
            catch { }
        }

        public static void Close()
        {
            try
            {
                var nsWindow = GetNSWindowFromScene();
                if (nsWindow == IntPtr.Zero) return;
                void_objc_msgSend(nsWindow, Selector.GetHandle("close"));
            }
            catch { }
        }
    }
}
