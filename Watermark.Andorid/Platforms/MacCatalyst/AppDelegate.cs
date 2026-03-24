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
        static extern void void_objc_msgSend_nint(IntPtr receiver, IntPtr selector, nint arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern void void_objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern nint nint_objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern IntPtr IntPtr_objc_msgSend_nint(IntPtr receiver, IntPtr selector, nint arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern void void_objc_msgSend_double(IntPtr receiver, IntPtr selector, double arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern void void_objc_msgSend_CGPoint(IntPtr receiver, IntPtr selector, CoreGraphics.CGPoint arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern IntPtr IntPtr_objc_msgSend_double_double_double_double(
            IntPtr receiver, IntPtr selector, double r, double g, double b, double a);

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
        /// 配置标题栏：隐藏标题文字，隐藏工具栏，隐藏原生红绿灯
        /// 必须在 window.Created 事件之后调用
        /// </summary>
        public static void ConfigureTitleBar()
        {
            var scene = GetWindowScene();
            if (scene == null) return;

            // 通过 UIWindowScene.Titlebar 隐藏标题文字
            if (scene.Titlebar is not null)
            {
                scene.Titlebar.TitleVisibility = UITitlebarTitleVisibility.Hidden;

                // 隐藏系统工具栏
                var toolbar = scene.Titlebar.Toolbar;
                if (toolbar != null)
                {
                    toolbar.Visible = false;
                }
            }

            // 设置最小窗口尺寸
            scene.SizeRestrictions.MinimumSize = new CoreGraphics.CGSize(900, 600);
        }

        /// <summary>
        /// 通过 NSWindow API 完全移除标题栏区域，让内容占满整个窗口
        /// </summary>
        public static void HideTrafficLights()
        {
            try
            {
                var nsWindow = GetNSWindowFromScene();
                if (nsWindow == IntPtr.Zero) return;

                // 获取当前 styleMask
                var currentMask = nint_objc_msgSend(nsWindow, Selector.GetHandle("styleMask"));

                // NSWindowStyleMask 常量
                nint titled = 1 << 0;              // NSWindowStyleMaskTitled
                nint fullSizeContentView = 1 << 15; // NSWindowStyleMaskFullSizeContentView

                // 去掉 titled（移除标题栏区域），加上 fullSizeContentView
                var newMask = (currentMask & ~titled) | fullSizeContentView;
                void_objc_msgSend_nint(nsWindow, Selector.GetHandle("setStyleMask:"), newMask);

                // 标题栏透明
                void_objc_msgSend_bool(nsWindow, Selector.GetHandle("setTitlebarAppearsTransparent:"), true);

                // 隐藏标题文字
                void_objc_msgSend_nint(nsWindow, Selector.GetHandle("setTitleVisibility:"), 1);

                // 允许通过窗口背景拖动
                void_objc_msgSend_bool(nsWindow, Selector.GetHandle("setMovableByWindowBackground:"), true);

                // 设置窗口背景色匹配内容区域（#F9FAFC），消除深色边框
                var nsColorClass = Class.GetHandle("NSColor");
                // NSColor colorWithRed:green:blue:alpha: — #F9FAFC
                var bgColor = IntPtr_objc_msgSend_double_double_double_double(
                    nsColorClass, Selector.GetHandle("colorWithRed:green:blue:alpha:"),
                    249.0 / 255.0, 250.0 / 255.0, 252.0 / 255.0, 1.0);
                void_objc_msgSend_IntPtr(nsWindow, Selector.GetHandle("setBackgroundColor:"), bgColor);

                // 确保窗口有阴影（类似微信的柔和阴影效果）
                void_objc_msgSend_bool(nsWindow, Selector.GetHandle("setHasShadow:"), true);

                // 窗口不透明
                void_objc_msgSend_bool(nsWindow, Selector.GetHandle("setOpaque:"), true);

                // 隐藏红绿灯按钮
                for (nint i = 0; i <= 2; i++)
                {
                    var button = IntPtr_objc_msgSend_nint(nsWindow, Selector.GetHandle("standardWindowButton:"), i);
                    if (button != IntPtr.Zero)
                    {
                        void_objc_msgSend_bool(button, Selector.GetHandle("setHidden:"), true);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 设置窗口圆角（参考微信 macOS 风格：10px 圆角）
        /// </summary>
        public static void SetCornerRadius(double radius = 10.0)
        {
            try
            {
                var nsWindow = GetNSWindowFromScene();
                if (nsWindow == IntPtr.Zero) return;

                var contentView = IntPtr_objc_msgSend(nsWindow, Selector.GetHandle("contentView"));
                if (contentView == IntPtr.Zero) return;

                // 确保 contentView 使用 layer
                void_objc_msgSend_bool(contentView, Selector.GetHandle("setWantsLayer:"), true);

                var cvLayer = IntPtr_objc_msgSend(contentView, Selector.GetHandle("layer"));
                if (cvLayer != IntPtr.Zero)
                {
                    void_objc_msgSend_double(cvLayer, Selector.GetHandle("setCornerRadius:"), radius);
                    // CALayer masksToBounds
                    void_objc_msgSend_bool(cvLayer, Selector.GetHandle("setMasksToBounds:"), true);
                }

                // 对 superview（NSThemeFrame）也设置圆角
                var superview = IntPtr_objc_msgSend(contentView, Selector.GetHandle("superview"));
                if (superview != IntPtr.Zero)
                {
                    void_objc_msgSend_bool(superview, Selector.GetHandle("setWantsLayer:"), true);
                    var svLayer = IntPtr_objc_msgSend(superview, Selector.GetHandle("layer"));
                    if (svLayer != IntPtr.Zero)
                    {
                        void_objc_msgSend_double(svLayer, Selector.GetHandle("setCornerRadius:"), radius);
                        void_objc_msgSend_bool(svLayer, Selector.GetHandle("setMasksToBounds:"), true);
                    }
                }

                // 确保阴影可见
                void_objc_msgSend_bool(nsWindow, Selector.GetHandle("setHasShadow:"), true);
                // 刷新阴影
                void_objc_msgSend(nsWindow, Selector.GetHandle("invalidateShadow"));
            }
            catch { }
        }

        /// <summary>
        /// 开始窗口拖动 - 通过移动 NSWindow frame 实现
        /// </summary>
        static CoreGraphics.CGPoint _dragStartMouseLocation;
        static CoreGraphics.CGRect _dragStartFrame;
        static bool _isDragging;

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern CoreGraphics.CGPoint CGPoint_objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern CoreGraphics.CGRect CGRect_objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        static extern void void_objc_msgSend_CGRect_bool_bool(IntPtr receiver, IntPtr selector, CoreGraphics.CGRect frame, [MarshalAs(UnmanagedType.I1)] bool display, [MarshalAs(UnmanagedType.I1)] bool animate);

        /// <summary>
        /// 获取当前鼠标在屏幕上的位置（NSEvent.mouseLocation）
        /// </summary>
        static CoreGraphics.CGPoint GetMouseLocation()
        {
            var nsEventClass = Class.GetHandle("NSEvent");
            return CGPoint_objc_msgSend(nsEventClass, Selector.GetHandle("mouseLocation"));
        }

        /// <summary>
        /// 获取 NSWindow 的 frame
        /// </summary>
        static CoreGraphics.CGRect GetWindowFrame(IntPtr nsWindow)
        {
            return CGRect_objc_msgSend(nsWindow, Selector.GetHandle("frame"));
        }

        /// <summary>
        /// 设置 NSWindow 的 frame
        /// </summary>
        static void SetWindowFrame(IntPtr nsWindow, CoreGraphics.CGRect frame)
        {
            void_objc_msgSend_CGRect_bool_bool(nsWindow, Selector.GetHandle("setFrame:display:animate:"), frame, true, false);
        }

        public static void StartDrag()
        {
            try
            {
                var nsWindow = GetNSWindowFromScene();
                if (nsWindow == IntPtr.Zero) return;
                _dragStartMouseLocation = GetMouseLocation();
                _dragStartFrame = GetWindowFrame(nsWindow);
                _isDragging = true;
            }
            catch { }
        }

        public static void DragMove()
        {
            if (!_isDragging) return;
            try
            {
                var nsWindow = GetNSWindowFromScene();
                if (nsWindow == IntPtr.Zero) return;
                var currentMouse = GetMouseLocation();
                var dx = currentMouse.X - _dragStartMouseLocation.X;
                var dy = currentMouse.Y - _dragStartMouseLocation.Y;
                var newFrame = _dragStartFrame;
                newFrame.X += dx;
                newFrame.Y += dy;
                SetWindowFrame(nsWindow, newFrame);
            }
            catch { }
        }

        public static void EndDrag()
        {
            _isDragging = false;
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
