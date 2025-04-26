using Masa.Blazor;
using Masa.Blazor.Presets;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SkiaSharp;
using System.Collections.Concurrent;
using System.Text;
using LukeMauiFilePicker;
using Watermark.Andorid.Models;
using Watermark.Razor;
using Microsoft.Maui.Controls.PlatformConfiguration;

namespace Watermark.Shared.Models
{
    public class ClientInstance : IClientInstance
    {
        public ClientInstance(IFilePickerService _picker)
        {
            picker = _picker;
        }
        private IFilePickerService picker;
        public string LinkPath { get; set; }
        public string UpdateMessage { get; set; }
        public string UpdateVersion { get; set; }

        public string Key()
        {
            var result = Convert.ToBase64String(Encoding.UTF8.GetBytes(GetAndroidId().Replace("-", "") + "CATLNMSL"));
            string result3 = result.Replace("-", "");
            return result3;
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

        public bool Save(byte[] b64, string fn)
        {
#if IOS
            Platforms.iOS.SavePictureService.SavePicture(b64);
#endif
            return true;
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
                    Global.Callback?.Invoke();
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

		public async Task<IEnumerable<string>> PickMultipleAsync()
		{
            var result = await picker.PickFilesAsync("pick", FileType, true);
            if (result == null) return [];
            return result.Select(x => x.FileResult?.FullPath);
        }

		public async Task<string> PickAsync()
		{
            var result = await picker.PickFileAsync("pick", FileType);
            return result?.FileResult?.FullPath ?? null;
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

        public Task<string> OpenFolder()
        {
            throw new NotImplementedException();
        }

        public void SetColor(string color = "#F5F5F5")
        {
        }

        public async Task<WMDesignFunc> GetWMDesignFunc(string canvasId)
        {

            var canvas = await Global.GetCanvas(canvasId);
            if (canvas is null) return null;
            canvas.Exif[canvasId] = ExifHelper.DefaultMeta;
            var design = new WMDesignFunc();
            design.CurrentCanvas = canvas;
            Func<WMLogo, Task> SelectLogo = new(async Task (mLogo) =>
            {
                var p = await PickAsync();
                if (!string.IsNullOrEmpty(p))
                {
                    var destFolder = Path.Combine(Global.AppPath.TemplatesFolder, canvasId);
                    if (!Directory.Exists(destFolder))
                    {
                        Directory.CreateDirectory(destFolder);
                    }
                    var name = Path.GetFileName(p);
                    var destFile = Path.Combine(destFolder, name);
                    File.Copy(p, destFile, true);
                    mLogo.Path = name;
                }
            });

            Func<WMContainer, Task> SelectContainer = new(async Task (mContainer) =>
            {
                var p = await PickAsync();
                if (!string.IsNullOrEmpty(p))
                {
                    var destFolder = Path.Combine(Global.AppPath.TemplatesFolder, canvasId);
                    if (!Directory.Exists(destFolder))
                    {
                        Directory.CreateDirectory(destFolder);
                    }
                    mContainer.Path = p;
                }
            });

            Func<Task<string>> SelectDefaultImage = new(async Task<string> () =>
            {
                var p = await PickAsync();
                if (!string.IsNullOrEmpty(p))
                {
                    var destFolder = Path.Combine(Global.AppPath.TemplatesFolder, canvasId);
                    if (!Directory.Exists(destFolder))
                    {
                        Directory.CreateDirectory(destFolder);
                    }

                    return p;
                }
                return "";
            });

            Func<Task> ImportFont = new Func<Task>(async Task () =>
            {
                var p = await PickAsync();
                if (!string.IsNullOrEmpty(p))
                {
                    var fontPath = Global.AppPath.FontFolder;
                    if (!Directory.Exists(fontPath))
                    {
                        Directory.CreateDirectory(fontPath);
                    }

                    var file = new FileInfo(p);
                    if (file.Exists)
                    {
                        try
                        {
                            var target = Path.Combine(fontPath, Path.GetFileName(p));
                            file.CopyTo(target, true);
                        }
                        catch { }
                        try
                        {
                            var waterPath = Path.Combine(Global.AppPath.TemplatesFolder, canvasId, Path.GetFileName(p));
                            file.CopyTo(waterPath, true);
                        }
                        catch { }
                    }
                }
            });

            design.SelectLogo = SelectLogo;
            design.SelectContainer = SelectContainer;
            design.SelectDefaultImageEvt = SelectDefaultImage;
            design.ImportFontEvt = ImportFont;
            return design;
        }

        public Task Update(Action<long, long> DownloadProgressChanged)
        {
            return Task.CompletedTask;
        }

        public Dictionary<DevicePlatform, IEnumerable<string>> FileType = new()
        {
            { DevicePlatform.Android, new[] { "text/*" } } ,
            { DevicePlatform.iOS, new[] { "image/jpg", "image/jpeg" } },
            { DevicePlatform.MacCatalyst, new[] { "image/jpg", "image/png" } },
            { DevicePlatform.WinUI, new[] { ".jpg", ".jepg" } }
        };
        public Dictionary<DevicePlatform, IEnumerable<string>> FileFontType = new()
        {
            { DevicePlatform.Android, new[] { ".ttf", ".otf" } } ,
            { DevicePlatform.iOS, new[] { ".ttf", ".otf" } },
            { DevicePlatform.MacCatalyst, new[] { ".ttf", ".otf" } },
            { DevicePlatform.WinUI, new[] { ".ttf", ".otf" } }
        };

        public Task<bool> Download(string directory, string fileName, string extension)
        {
            return Task.Run(()=> true);
        }

        public void Exit()
        {
        }

        public void OpenDesign(WMCanvas canvas)
        {
        }

        public Task InteropInit(string appId)
        {
            return Task.CompletedTask;
        }

        public string AppTitle { get; set; } = "";
	}
}
