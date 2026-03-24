using Masa.Blazor;
using Masa.Blazor.Presets;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using CommunityToolkit.Maui.Storage;
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
#elif MACCATALYST
            return GetMacSerialNumber();
#else
            return Guid.NewGuid().ToString();
#endif
        }

#if MACCATALYST
        private static string GetMacSerialNumber()
        {
            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "/usr/sbin/ioreg";
                process.StartInfo.Arguments = "-l | grep IOPlatformSerialNumber";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;

                // ioreg doesn't support piping, use shell
                process.StartInfo.FileName = "/bin/bash";
                process.StartInfo.Arguments = "-c \"ioreg -l | grep IOPlatformSerialNumber\"";
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Parse: "IOPlatformSerialNumber" = "XXXXXXXXXXXX"
                if (!string.IsNullOrEmpty(output))
                {
                    var parts = output.Split('=');
                    if (parts.Length > 1)
                    {
                        return parts[1].Trim().Trim('"').Trim();
                    }
                }
            }
            catch { }
            // Fallback to a stable identifier based on machine name
            return Environment.MachineName + "-" + Environment.UserName;
        }
#endif


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
#if MACCATALYST
                client = "WatermarkMac";
#endif
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
#if ANDROID || IOS
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
#endif
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
#if ANDROID || IOS
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
#endif
            // macOS doesn't have haptic feedback
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
#if ANDROID
            var rs = await api.GetPayToken(cost, tradeName);
            if (rs != null && rs.success && !string.IsNullOrEmpty(rs.data))
            {
                API<string> jt = await Task.Run(() =>
                {
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
#else
            // macOS/iOS: payment not supported in this build
            return new API<string> { success = false, message = new APISub { content = "当前平台暂不支持支付" } };
#endif
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
#elif MACCATALYST
            try
            {
                var outputPath = Global.OutPutPath;
                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);
                var filePath = Path.Combine(outputPath, "DFX_" + fn);
                File.WriteAllBytes(filePath, b64);
                return true;
            }
            catch
            {
                return false;
            }
#else
            return true;
#endif
        }

        public async Task<string> OpenFolder()
        {
            try
            {
                var result = await FolderPicker.Default.PickAsync();
        
                if (result != null)
                {
                    return result.IsSuccessful ? result.Folder.Path : "";
                }
            }
            catch (Exception e)
            {
            }

            return null;
        }

        public void SetColor(string color = "#F5F5F5")
        {
#if ANDROID
            MainActivity.SetColor?.Invoke(color);
#endif
            // macOS doesn't need status bar color changes
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
                if (!string.IsNullOrEmpty(name))
                {
#if MACCATALYST
                    var destFolder = Path.Combine(Global.AppPath.TemplatesFolder, canvasId);
                    if (!Directory.Exists(destFolder))
                        Directory.CreateDirectory(destFolder);
                    var fileName = Path.GetFileName(name);
                    var destFile = Path.Combine(destFolder, fileName);
                    File.Copy(name, destFile, true);
                    x.Path = fileName;
#else
                    x.Path = name;
#endif
                }
            });

            design.SelectContainer = new Func<WMContainer, Task>(async Task (x) =>
            {
                var name = await PickAsync();
                if (!string.IsNullOrEmpty(name))
                    x.Path = name;
            });

            design.SelectDefaultImageEvt = PickAsync;

            design.ImportFontEvt = new Func<Task>(async () =>
            {
#if MACCATALYST
                try
                {
                    var result = await FilePicker.PickAsync(new PickOptions
                    {
                        PickerTitle = "选择字体文件",
                        FileTypes = new FilePickerFileType(MacOS.FileFontType)
                    });
                    if (result != null)
                    {
                        var fontPath = Global.AppPath.FontFolder;
                        if (!Directory.Exists(fontPath))
                            Directory.CreateDirectory(fontPath);
                        var target = Path.Combine(fontPath, Path.GetFileName(result.FullPath));
                        File.Copy(result.FullPath, target, true);
                        try
                        {
                            var waterPath = Path.Combine(Global.AppPath.TemplatesFolder, canvasId, Path.GetFileName(result.FullPath));
                            File.Copy(result.FullPath, waterPath, true);
                        }
                        catch { }
                    }
                }
                catch { }
#endif
                await Task.CompletedTask;
            });
            design.ImportFontEvt2 = new Func<string, Task>(async (id) =>
            {
#if MACCATALYST
                try
                {
                    var result = await FilePicker.PickAsync(new PickOptions
                    {
                        PickerTitle = "选择字体文件",
                        FileTypes = new FilePickerFileType(MacOS.FileFontType)
                    });
                    if (result != null)
                    {
                        var fontPath = Global.AppPath.FontFolder;
                        if (!Directory.Exists(fontPath))
                            Directory.CreateDirectory(fontPath);
                        var target = Path.Combine(fontPath, Path.GetFileName(result.FullPath));
                        File.Copy(result.FullPath, target, true);
                        try
                        {
                            var waterPath = Path.Combine(Global.AppPath.TemplatesFolder, id, Path.GetFileName(result.FullPath));
                            File.Copy(result.FullPath, waterPath, true);
                        }
                        catch { }
                    }
                }
                catch { }
#endif
                await Task.CompletedTask;
            });

            design.HotKeyEvt = new Action<Action>((x) =>
            {
                // macOS hotkey registration not needed in MAUI Blazor Hybrid
            });
            return design;
        }

        public async Task Update(Action<long, long> DownloadProgressChanged)
        {
#if ANDROID
            Global.APK = DateTime.Now.ToString("yyyyMMddHHmmss") + ".apk";
            await upgradeService.DownloadFileAsync(LinkPath, DownloadProgressChanged);
            upgradeService.InstallNewVersion();
#elif MACCATALYST
            // On macOS, open the download link in browser
            if (!string.IsNullOrEmpty(LinkPath))
            {
                await Browser.Default.OpenAsync(LinkPath, BrowserLaunchMode.SystemPreferred);
            }
#endif
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
#if MACCATALYST
            WindowHelper.Close();
#endif
        }

        public void OpenDesign(WMCanvas canvas)
        {
        }

        public Task InteropInit(string appId)
        {
            return Task.CompletedTask;
        }

        public string AppTitle { get; set; } = "轻影";

        public void WindowMinimize()
        {
#if MACCATALYST
            WindowHelper.Minimize();
#endif
        }

        public void WindowZoom()
        {
#if MACCATALYST
            WindowHelper.Zoom();
#endif
        }

        public void WindowClose()
        {
#if MACCATALYST
            WindowHelper.Close();
#endif
        }

        public void WindowStartDrag()
        {
#if MACCATALYST
            WindowHelper.StartDrag();
#endif
        }

        public void WindowDragMove()
        {
#if MACCATALYST
            WindowHelper.DragMove();
#endif
        }

        public void WindowEndDrag()
        {
#if MACCATALYST
            WindowHelper.EndDrag();
#endif
        }
    }
}
