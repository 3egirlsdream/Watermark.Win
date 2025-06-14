﻿using Masa.Blazor;
using Masa.Blazor.Presets;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Watermark.Andorid;
using Watermark.Andorid.Models;
using Watermark.Razor;

namespace Watermark.Shared.Models
{
    public class ClientInstance : IClientInstance
    {
        readonly APIHelper api;
        readonly IUpgradeService upgradeService;
        public ClientInstance(APIHelper api, IUpgradeService upgradeService) 
        {
            this.api = api;
            this.upgradeService = upgradeService;
        }   
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


        async Task<Dictionary<string, int>> InitVersion(List<string> ids)
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

        static async Task<WMZipedTemplate> LoadSingleTemplates(
          string watermarkId
          , IWMWatermarkHelper helper
          , IPopupService PopupService
          , IJSRuntime JSRuntime
          , PageStackNavController NavController)
        {
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
                    return Global.ReadConfigFromPath(configPath);
                });
                dirct.WMCanvas = canvas;
                dirct.WMCanvas.Exif[canvas.ID] = ExifHelper.DefaultMeta;
                dirct.CanvasType = dirct.WMCanvas.CanvasType;
                await Global.InitFonts([canvas]);
                var b64 = await helper.GenerationAsync(canvas, null, true, false);
                dirct.Src = await Global.Byte2Url(JSRuntime, b64);
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
                    Versions = await InitVersion([watermarkId]);
                    Global.Callback?.Invoke();
                }
            });
            if (Global.CurrentUser == null || string.IsNullOrEmpty(Global.CurrentUser.ID))
            {
                parameter.FocusImageShow = false;
                NavController.Push("/login");
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
            var result = await FilePicker.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "长按多选照片",
                FileTypes = FilePickerFileType.Images
            });
            return result?.Select(x => x.FullPath) ?? [];
        }

        public async Task<string> PickAsync()
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "选择照片",
                FileTypes = FilePickerFileType.Images
            });
            return result?.FullPath ?? string.Empty;
        }

        public async Task SetTextAsync(string uri)
        {
            await Clipboard.Default.SetTextAsync(uri);
        }

        public async Task<API<string>> AliPays(decimal cost, string tradeName)
        {
            var rs = await api.GetPayToken(cost, tradeName);
            if (rs != null && rs.success && !string.IsNullOrEmpty(rs.data))
            {
                API<string> jt = await Task.Run(() =>
                {
#if ANDROID
                    string con = rs.data;
                    var act = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                    var pa = new Com.Alipay.Sdk.App.PayTask(act);
                    var payRs = pa.Pay(con, true);
                    try
                    {
                        string json = payRs.ToString();
                        if (json.StartsWith("resultStatus={9000}"))
                        {
                            var start = json.IndexOf("result={");
                            var content = json.Substring(start + 8);
                            var end = content.LastIndexOf("};extendInfo=");
                            content = content.Substring(0, end);
                            return new API<string> { success = true, data = content };
                        }
                        else return new API<string> { success = false, message = new APISub { content = $"支付失败：错误代码" } };
                    }
                    catch (Exception ex)
                    {
                        return new API<string> { message = new APISub { content = ex.Message }, success = false };

                    }
#else
                    return new API<string> { };
#endif
                });
                if (!jt.success) return jt;

                JObject result = JObject.Parse(jt.data);
                var r = result?["alipay_trade_app_pay_response"];
                if (r == null) return new API<string> { success = false, message = new APISub { content = "API返回为空" } };
                var code = r["code"]?.ToString();
                var msg = r["msg"]?.ToString();
                var app_id = r["app_id"]?.ToString();
                var auth_app_id = r["auth_app_id"]?.ToString();
                var out_trade_no = r["out_trade_no"]?.ToString(); ;
                var total_amount = r["total_amount"]?.ToString();
                var trade_no = r["trade_no"]?.ToString();
                var seller_id = r["seller_id"]?.ToString();
                var up = await api.RecordBill(code, msg, app_id, auth_app_id, out_trade_no, trade_no, tradeName, total_amount, seller_id);
                if (up == null || !up.success) return new API<string> { success = false, message = new APISub { content = up?.message?.content ?? "" } };

                return new API<string> { success = true, data = "支付成功" };
            }
            else
            {
                return rs;
            }

        }

        public async Task ReLogin()
        {
            var result = await Global.ReadLocalAsync();
            if (!string.IsNullOrEmpty(result.Item1))
            {
                var login = await api.LoginIn(result.Item1, result.Item2, true);
                if (login.success)
                {
                    Global.CurrentUser = Global.SetUserInfo(login.data.data);
                }
            }
        }

        public bool Save(byte[] b64, string fn)
        {
#if ANDROID
            return Watermark.Andorid.SavePictureService.SavePicture(b64, "DFX_" + fn);
#endif
            return true;
        }

        public Task<string> OpenFolder()
        {
            throw new NotImplementedException();
        }

        public void SetColor(string color = "#F5F5F5")
        {
#if ANDROID
            MainActivity.SetColor?.Invoke(color);
#endif
        }

        public async Task<WMDesignFunc> GetWMDesignFunc(string canvasId)
        {
            var canvas = await Global.GetCanvas(canvasId);
            if (canvas is null) return null;
            canvas.Exif[canvasId] = ExifHelper.DefaultMeta;
            var design = new WMDesignFunc();
            design.CurrentCanvas = canvas;
            design.SelectLogo = new Func<WMLogo, Task>(async Task (x) =>
            {
                var name = await PickAsync();
                x.Path = name;
            });

            design.SelectContainer = new Func<WMContainer, Task>(async Task (x) =>
            {
                var name = await PickAsync();
                x.Path = name;
            });

            design.SelectDefaultImageEvt = PickAsync;

            design.ImportFontEvt = new Func<Task>(() =>
            {
                return Task.Run(() =>
                { 
                });
            });
            design.ImportFontEvt2 = new Func<string, Task>((id) =>
            {
                return Task.Run(() =>
                {
                });
            });

            design.HotKeyEvt = new Action<Action>((x) =>
            {
            });
            return design;
        }

        public async Task Update(Action<long, long> DownloadProgressChanged)
        {
            Global.APK = DateTime.Now.ToString("yyyyMMddHHmmss") + ".apk";
            await upgradeService.DownloadFileAsync(LinkPath, DownloadProgressChanged);
            upgradeService.InstallNewVersion();

        }

        public async Task<bool> Download(string directory, string fileName, string extension)
        {
            try
            {
                using (var stream = await FileSystem.OpenAppPackageFileAsync($"{fileName}.{extension}"))
                {
                    if (!Directory.Exists(Global.AppPath.TemplatesFolder))
                    {
                        Directory.CreateDirectory(Global.AppPath.TemplatesFolder);
                    }

                    if(extension == "zip")
                    {
                        var target = Path.Combine(directory, fileName);
                        if (Directory.Exists(target))
                        {
                            Directory.Delete(target, true);
                        }
                        Directory.CreateDirectory(target);
                        await Task.Run(() => ZipFile.ExtractToDirectory(stream, target, true));
                    }
                    else if (new string[] { "otf", "ttf" }.Contains(extension))
                    {
                        var target = Path.Combine(directory, fileName, extension);
                        using var fs = new FileStream(target, FileMode.Create);
                        stream.CopyTo(fs);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
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
