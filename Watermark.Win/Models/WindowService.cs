using Microsoft.JSInterop;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Application = System.Windows.Application;


namespace Watermark.Win.Models
{
    public class WindowService : IWindowService
    {
        private static bool _isMoving;
        private static double _startMouseX;
        private static double _startMouseY;
        private static double _startWindLeft;
        private static double _startWindTop;
        private static Tuple<decimal, decimal> point = new(1, 1);
        public WindowService()
        {
            point = GetScreenScalingFactor();
        }
        [JSInvokable]
        public static void StartMove()
        {
            _isMoving = true;
            _startMouseX = GetX();
            _startMouseY = GetY();
            
            var window = GetActiveWindow();
            if (window == null)
            {
                return;
            }
            _startWindLeft = window.Left;
            _startWindTop = window.Top;
        }

        [JSInvokable]
        public static void StopMove()
        {
            _isMoving = false;
        }


        [JSInvokable]
        public static void UpdateWindowPos()
        {
            if (!_isMoving)
            {
                return;
            }

            double moveX = GetX() - _startMouseX;
            double moveY = GetY() - _startMouseY;
            Window? window = GetActiveWindow();
            if (window == null)
            {
                return;
            }

            window.Left = _startWindLeft + moveX;
            window.Top = _startWindTop + moveY;
        }

        public void Minimize()
        {
            var window = GetActiveWindow();
            if (window != null)
            {
                window.WindowState = WindowState.Minimized;
            }
        }

        public void Maximize()
        {
            var window = GetActiveWindow();
            if (window != null)
            {
                window.WindowState =
                    window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
        }


        public bool IsMaximized()
        {
            var window = GetActiveWindow();
            if (window != null)
            {
                return window.WindowState == WindowState.Maximized;
            }

            return false;
        }

        public void Close(bool allWindow = false)
        {
            if (allWindow)
            {
                Application.Current?.Shutdown();
                return;
            }

            var window = GetActiveWindow();
            if (window != null)
            {
                window.Close();
            }
        }

        private static int GetX()
        {
            return (int)(Control.MousePosition.X / point.Item1);
        }

        private static int GetY()
        {
            return (int)(Control.MousePosition.Y / point.Item2);
        }

        private static Window? GetActiveWindow()
        {
            return Application.Current.Windows.Cast<Window>().FirstOrDefault(currentWindow => currentWindow.IsActive);
        }


        private static Tuple<decimal, decimal> GetScreenScalingFactor()
        {
            var dpiXProperty = typeof(SystemParameters).GetProperty("DpiX", BindingFlags.NonPublic | BindingFlags.Static);
            var dpiYProperty = typeof(SystemParameters).GetProperty("Dpi", BindingFlags.NonPublic | BindingFlags.Static);
            var dpiX = (int)dpiXProperty.GetValue(null, null);
            var dpiY = (int)dpiYProperty.GetValue(null, null);
            var dpixRatio = dpiX / 96M;
            var dpiyRatio = dpiY / 96M;
            return Tuple.Create(dpixRatio, dpiyRatio);
        }
    }
}
