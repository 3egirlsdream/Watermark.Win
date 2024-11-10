﻿using Masa.Blazor;
using Masa.Blazor.Presets;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SkiaSharp;
using System.Collections.Concurrent;
using System.Text;
using Watermark.Andorid.Models;
using Watermark.Razor;

namespace Watermark.Shared.Models
{
    public class ClientInstance : IClientInstance
    {
        public string LinkPath { get; set; }
        public string UpdateMessage { get; set; }
        public string UpdateVersion { get; set; }
        public Action<WMCanvas, WMLogo, Dictionary<string, string>> SelectImageAction = (canvas, mLogo, ImagesBase64) =>
        {

        };

        public Action<WMCanvas, WMContainer, ConcurrentDictionary<string, string>> SelectContainerImageAction = (canvas, mContainer, ImagesBase64) =>
        {
        };



        public Action<List<string>> InitLocalFontsAction = (Fonts) =>
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


        public Action<List<string>> ImportLocalFontAction = (Fonts) =>
        {
        };

        public Action<WMCanvas, WMText, string> SelectLocalFontAction = (CurrentCanvas, mText, fontName) =>
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

        public void SelectDefaultImage(string id, ConcurrentDictionary<string, string> dic)
        {
        }

        public void WriteThumbnailImage(SKBitmap source, string target)
        {
            double w = source.Width, h = source.Height;
            var xs = 1080.0 / h;
            var resized = source.Resize(new SkiaSharp.SKImageInfo((int)(w * xs), (int)(h * xs)), SkiaSharp.SKFilterQuality.Low);
            using var image = SKImage.FromBitmap(resized);
            using var writeStream = File.OpenWrite(target);
            image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80).SaveTo(writeStream);
        }

        public string Key()
        {
            var result = Convert.ToBase64String(Encoding.UTF8.GetBytes(GetAndroidId().Replace("-", "") + "CATLNMSL"));
            string result3 = result.Replace("-", "");
            return result3;
        }

        public void OpenSetting()
        {
        }

        public void ShowMsg(ISnackbar snackbar, string message, Severity severity)
        {
            snackbar.Configuration.PositionClass = Defaults.Classes.Position.TopCenter;
            snackbar?.Add(message, severity, config =>
              {
                  config.ShowCloseIcon = false;
              });
        }

        public Version GetVersion()
        {
            return AppInfo.Version;
        }

        /// <summary>
        /// 获取设备号
        /// </summary>
        /// <returns></returns>
        public string GetAndroidId()
        {
#if ANDROID
      var context = Android.App.Application.Context;
      var deviceId = Android.Provider.Settings.Secure.GetString(context.ContentResolver, Android.Provider.Settings.Secure.AndroidId);
      return deviceId;
#endif
            return Guid.NewGuid().ToString();
        }


        public async Task<bool> IsOutOfDate(string client = "Watermark_A")
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

        public async Task<bool> CheckUpdate(string client = "WatermarkAndroid")
        {
            try
            {
                var version = await Connections.HttpGetAsync<WMClientVersion>(APIHelper.HOST + $"/api/CloudSync/GetVersion?Client={client}", Encoding.Default);
                if (version != null && version.success && version.data != null && version.data.VERSION != null)
                {
                    var v1 = GetVersion();
                    var v2 = new Version(version.data.VERSION);
                    LinkPath = version!.data!.PATH!;
                    UpdateMessage = version.data.MEMO ?? "";
                    UpdateVersion = version!.data!.VERSION;
                    return v2 > v1;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }



        async Task<Dictionary<string, int>> InitVersion(List<string> ids, APIHelper api)
        {
            Dictionary<string, int> Versions = [];
            var version = await api.GetVersions(ids);
            if (version.success && version.data != null)
            {
                foreach (var e in version.data)
                {
                    Versions[e.Key] = e.Value;
                }
            }
            return Versions;
        }

        async Task<WMZipedTemplate> LoadSingleTemplates(
          string watermarkId
          , IWMWatermarkHelper helper
          , IPopupService PopupService
          , IJSRuntime JSRuntime
          , PageStackNavController NavController)
        {
            var api = new APIHelper();
            if (!Directory.Exists(Global.AppPath.TemplatesFolder))
            {
                Directory.CreateDirectory(Global.AppPath.TemplatesFolder);
            }

            try
            {
                WMZipedTemplate dirct = new();
                dirct.WatermarkId = watermarkId;
                var configPath = Global.AppPath.TemplatesFolder + watermarkId + System.IO.Path.DirectorySeparatorChar + "config.json";
                if (!System.IO.File.Exists(configPath)) return dirct;
                var canvas = await Task.Run(() =>
                {
                    var content = File.ReadAllText(configPath);
                    return Global.ReadConfig(content);
                });
                dirct.WMCanvas = canvas;
                dirct.WMCanvas.Exif[canvas.ID] = ExifHelper.DefaultMeta;
                dirct.CanvasType = dirct.WMCanvas.CanvasType;
                await Global.InitFonts([canvas]);
                var b64 = await helper.GenerationAsync(canvas, null, true, false);
                dirct.Src = await JSRuntime.InvokeAsync<string>("byteToUrl", b64);
                return dirct;
            }
            catch (Exception ex)
            {
                Common.ShowMsg(PopupService, ex.Message, AlertTypes.Error);
                return new WMZipedTemplate();
            }
        }



        public async Task DownloadTemplate(
          string watermarkId
          , ViewParameter parameter
          , IPopupService PopupService
          , List<WMZipedTemplate> ZipedTemplates
          , IWMWatermarkHelper helper
          , IJSRuntime JSRuntime
          , Dictionary<string, int> Versions
          , PageStackNavController NavController
          , FailedBox failedBox)
        {
            var action = new Action(async () =>
            {
                var api = new APIHelper();
                if (!Global.CurrentUser.IsVIP)
                {
                    var auth = await api.DownloadTemplate(Global.CurrentUser?.ID ?? "", watermarkId);
                    if (!auth.success)
                    {
                        parameter.FocusImageShow = false;
                        failedBox.faildShow = true;
                        failedBox.failedMessage = auth.message.content;
                        await PublicMethods.ReLogin();
                        return;
                    }
                }

                var w = ZipedTemplates.FirstOrDefault(x => x.WatermarkId == watermarkId);
                var r = await api.Download(watermarkId, w?.UserId ?? "");
                if (r)
                {
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                    parameter.FocusImageShow = false;
                    Common.ShowMsg(PopupService, "下载完成", AlertTypes.Success);
                    var dir = await LoadSingleTemplates(watermarkId, helper, PopupService, JSRuntime, NavController);
                    if (!GlobalCache.DownloadedTemplates.Any()) GlobalCache.DownloadedTemplates.Add(dir);
                    else if (!GlobalCache.DownloadedTemplates.Any(x => x.WatermarkId == dir.WatermarkId)) GlobalCache.DownloadedTemplates.Insert(0, dir);
                    else if (GlobalCache.DownloadedTemplates.Any(x => x.WatermarkId == dir.WatermarkId))
                    {
                        var idx = GlobalCache.DownloadedTemplates.FindIndex(x => x.WatermarkId == dir.WatermarkId);
                        var item = GlobalCache.DownloadedTemplates.First(x => x.WatermarkId == dir.WatermarkId);
                        GlobalCache.DownloadedTemplates.Remove(item);
                        GlobalCache.DownloadedTemplates.Insert(idx, dir);
                    }
                    Versions = await InitVersion([watermarkId], api);
                }
            });
            if (Global.CurrentUser == null || string.IsNullOrEmpty(Global.CurrentUser.ID))
            {
                parameter.FocusImageShow = false;
                //DialogOptions topCenter = new DialogOptions() { NoHeader = true, FullScreen = true };
                //var rst = DialogService.Show<Watermark.Razor.Components.LoginDialog>("", topCenter);
                NavController.Push("/login");
                // var dialogResult = await rst.Result;
                // if (!dialogResult.Canceled && dialogResult.Data.Equals(true))
                // {
                //   FocusImageShow = false;
                //   action.Invoke();
                // }
                return;
            }
            var p = Global.AppPath.TemplatesFolder + watermarkId;
            if (Directory.Exists(p))
            {
                parameter.FocusImageShow = false;
                var confirmed = await PopupService.ConfirmAsync("确认覆盖", "此模板已存在，确定覆盖？", AlertTypes.Info);
                if (confirmed == true)
                {
                    Directory.Delete(p, true);
                    action.Invoke();
                }
            }
            else
            {
                action.Invoke();
            }
        }

        public void Haptic()
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }

		public Task<IEnumerable<string>> PickMultipleAsync()
		{
			throw new NotImplementedException();
		}

		public Task<string> PickAsync()
		{
			throw new NotImplementedException();
		}

		public async Task SetTextAsync(string uri)
		{
			await Clipboard.Default.SetTextAsync(uri);
		}

		public async Task<API<string>> AliPays(decimal cost, string tradeName)
		{
			var api = new APIHelper();
			var rs = await api.GetPayToken(cost, tradeName);
			return rs;
		}

		public async Task ReLogin()
		{
			APIHelper helper = new APIHelper();
			var result = await Global.ReadLocalAsync();
			if (!string.IsNullOrEmpty(result.Item1))
			{
				var login = await helper.LoginIn(result.Item1, result.Item2, true);
				if (login.success)
				{
					Global.CurrentUser = Global.SetUserInfo(login.data.data);
				}
			}
		}
	}
}
