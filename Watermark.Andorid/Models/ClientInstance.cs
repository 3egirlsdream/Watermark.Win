using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SkiaSharp;
using System.Collections.Concurrent;
using System.Text;
using Watermark.Win.Models;

namespace Watermark.Shared.Models
{
	public static class ClientInstance
    {
        public static string LinkPath { get; set; }
        public static string UpdateMessage { get; set; }
		public static string UpdateVersion { get; set; }
		public static Action<WMCanvas, WMLogo, Dictionary<string, string>> SelectImageAction = (canvas, mLogo, ImagesBase64) =>
        {
            
        };

        public static Action<WMCanvas, WMContainer, ConcurrentDictionary<string, string>> SelectContainerImageAction = (canvas, mContainer, ImagesBase64) =>
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

        public static void SelectDefaultImage(string id, ConcurrentDictionary<string, string> dic)
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


        public static async Task<bool> IsOutOfDate(string client = "Watermark_A")
        {
            var version = await Connections.HttpGetAsync<WMClientVersion>(APIHelper.HOST + $"/api/CloudSync/GetVersion?Client={client}", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.VERSION != null)
            {
                var v1 = GetVersion();
                var v2 = new Version(version.data.VERSION);
                if (v2 > v1) return true;
            }
            return false;
        }

        public static async Task<bool> CheckUpdate()
        {
            try
            {
                var version = await Connections.HttpGetAsync<WMClientVersion>(APIHelper.HOST + "/api/CloudSync/GetVersion?Client=WatermarkAndroid", Encoding.Default);
                if (version != null && version.success && version.data != null && version.data.VERSION != null)
                {
                    var v1 = GetVersion();
                    var v2 = new Version(version.data.VERSION);
                    LinkPath = version!.data!.PATH!;
                    UpdateMessage = version!.data!.MEMO;
                    UpdateVersion = version!.data!.VERSION;
                    return v2 > v1;
                }
                else
                {
                    return false;
                }
            }
            catch(Exception ex)
            {
                return false;
            }
        }

        public static async Task Update(Action<int> action)
        {
           
        }

        public static Dictionary<DevicePlatform, IEnumerable<string>> FileType = new()
            {
                {  DevicePlatform.Android, new[] { "text/*" } } ,
                { DevicePlatform.iOS, new[] { "public.json", "public.plain-text" } },
                { DevicePlatform.MacCatalyst, new[] { "image/jpg", "image/png" } },
                { DevicePlatform.WinUI, new[] { ".jpg", ".jepg" } }
            };
    }
	public static class Ext
	{
		public static void Back(this NavigationManager navigation)
		{
			var builder = MauiApp.CreateBuilder();
			var jsruntime = builder.Services.BuildServiceProvider().GetService<IJSRuntime>();
			jsruntime!.InvokeVoidAsync("history.back");
		}
	}
}
