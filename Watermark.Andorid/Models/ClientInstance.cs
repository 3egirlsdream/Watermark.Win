using Microsoft.Maui.Controls.PlatformConfiguration;
using MudBlazor;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Watermark.Andorid;
using Watermark.Win.Models;

namespace Watermark.Shared.Models
{
    public static class ClientInstance
    {

        public static Action<WMCanvas, WMLogo, Dictionary<string, string>> SelectImageAction = (canvas, mLogo, ImagesBase64) =>
        {
        };

        public static Action<WMCanvas, WMContainer, Dictionary<string, string>> SelectContainerImageAction = (canvas, mContainer, ImagesBase64) =>
        {
        };



        public static Action<List<string>> InitLocalFontsAction = (Fonts) =>
        {
            Fonts ??= [];
            Fonts.Clear();
            var fontPath = AppDomain.CurrentDomain.BaseDirectory + "fonts";
            if (Directory.Exists(fontPath))
            {
                var files = Directory.GetFiles(fontPath);
                foreach (var file in files)
                {
                    Fonts.Add(System.IO.Path.GetFileName(file));
                }
            }
        };


        public static Action<List<string>> ImportLocalFontAction = (Fonts) =>
        {
        };

        public static Action<WMCanvas, WMText, string> SelectLocalFontAction = (CurrentCanvas, mText, fontName) =>
        {
            var fontPath = AppDomain.CurrentDomain.BaseDirectory + "fonts" + System.IO.Path.DirectorySeparatorChar + fontName;
            var targetPath = Global.AppPath.TemplatesFolder + CurrentCanvas.ID + Path.DirectorySeparatorChar + fontName;
            var file = new FileInfo(fontPath);
            if (file.Exists)
            {
                try
                {
                    file.CopyTo(targetPath, true);
                    mText.FontFamily = fontName;
                }
                catch { }
            }
        };

        public static void SelectDefaultImage(string id, Dictionary<string, string> dic)
        {
        }

        public static void WriteThumbnailImage(SKBitmap source, string target)
        {
            double w = source.Width, h = source.Height;
            var xs = 1080.0 / h;
            var resized = source.Resize(new SkiaSharp.SKImageInfo((int)(w * xs), (int)(h * xs)), SkiaSharp.SKFilterQuality.Low);
            using var image = SKImage.FromBitmap(resized);
            using var writeStream = File.OpenWrite(target);
            image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80).SaveTo(writeStream);
        }

        public static string Key()
        {
            var result = Convert.ToBase64String(Encoding.UTF8.GetBytes(GetAndroidId().Replace("-", "") + "CATLNMSL"));
            string result3 = result.Replace("-", "");
            return result3;
        }

        public static void OpenSetting()
        {
        }

        public static void ShowMsg(ISnackbar snackbar, string message, Severity severity)
        {
            snackbar.Configuration.PositionClass = Defaults.Classes.Position.TopCenter;
            snackbar?.Add(message, severity, config =>
                {
                    config.ShowCloseIcon = false;
                });
        }

        public static Version GetVersion()
        {
            return AppInfo.Version;
        }

        /// 获取设备号
        /// </summary>
        /// <returns></returns>
        public static string GetAndroidId()
        {
#if ANDROID
            var context = Android.App.Application.Context;
            var deviceId = Android.Provider.Settings.Secure.GetString(context.ContentResolver, Android.Provider.Settings.Secure.AndroidId);
            return deviceId;
#endif
            return "";
        }


        public static async Task<bool> IsOutOfDate()
        {
            var version = await Connections.HttpGetAsync<WMClientVersion>(APIHelper.HOST + "/api/CloudSync/GetVersion?Client=Watermark_A", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.VERSION != null)
            {
                var v1 = GetVersion();
                var v2 = new Version(version.data.VERSION);
                if (v2 > v1) return true;
            }
            return false;
        }
    }
}
