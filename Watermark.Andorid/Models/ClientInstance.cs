using Microsoft.AspNetCore.Components;
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
#elif IOS
            var vendorId = UIKit.UIDevice.CurrentDevice.IdentifierForVendor?.AsString();
            if (!string.IsNullOrWhiteSpace(vendorId)) return vendorId;

            const string preferenceKey = "Watermark.DeviceIdentifier";
            var persistedId = Preferences.Default.Get(preferenceKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(persistedId)) return persistedId;
            persistedId = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(preferenceKey, persistedId);
            return persistedId;
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
                FileTypes = new FilePickerFileType(MacOS.FileType)
            });
            if (result is null) return [];
            var paths = new List<string>();
            foreach (var file in result) paths.Add(await ResolvePickedPhotoAsync(file));
            return paths.Where(File.Exists);
        }

        public async Task<string> PickAsync()
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "选择照片",
                FileTypes = new FilePickerFileType(MacOS.FileType)
            });
            return result is null ? string.Empty : await ResolvePickedPhotoAsync(result);
        }

        private static async Task<string> ResolvePickedPhotoAsync(FileResult file)
        {
#if MACCATALYST || WINDOWS
            if (!string.IsNullOrWhiteSpace(file.FullPath) && File.Exists(file.FullPath)) return file.FullPath;
#endif
            var directory = Path.Combine(FileSystem.CacheDirectory, "photo-imports");
            Directory.CreateDirectory(directory);
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var destination = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
            await using var source = await file.OpenReadAsync();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.Asynchronous);
            await source.CopyToAsync(output);
            return destination;
        }

        public async Task SetTextAsync(string uri)
        {
            await Clipboard.Default.SetTextAsync(uri);
        }

        public async Task<API<string>> AliPays(decimal cost, string tradeName)
        {
#if ANDROID
            var userId = Global.CurrentUser?.ID ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new API<string>
                {
                    success = false,
                    message = new APISub { content = "请先登录后再开通会员" }
                };
            }

            var rs = await api.GetPayToken(cost, tradeName, userId);
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
                var response = result?["alipay_trade_app_pay_response"];
                var outTradeNo = response?["out_trade_no"]?.ToString();
                if (string.IsNullOrWhiteSpace(outTradeNo))
                {
                    return new API<string>
                    {
                        success = false,
                        message = new APISub { content = "支付宝未返回订单号" }
                    };
                }

                API<DesktopPayStatus>? confirmed = null;
                var sawPending = false;
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    confirmed = await api.QueryPay(outTradeNo);
                    if (confirmed?.success == true
                        && string.Equals(confirmed.data?.Status, "PAID", StringComparison.OrdinalIgnoreCase))
                    {
                        return new API<string> { success = true, data = "支付成功" };
                    }

                    var status = confirmed?.data?.Status;
                    sawPending |= confirmed?.success == true
                        && string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase);
                    if (string.Equals(status, "CLOSED", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        return new API<string>
                        {
                            success = false,
                            message = new APISub
                            {
                                content = confirmed?.data?.Message ?? "支付未完成"
                            }
                        };
                    }

                    if (attempt < 4)
                    {
                        await Task.Delay(1000);
                    }
                }

                if (sawPending)
                {
                    return new API<string>
                    {
                        success = true,
                        data = "支付已完成，会员状态确认中，请勿重复付款"
                    };
                }

                var queryMessage = confirmed?.message?.content;
                return new API<string>
                {
                    success = false,
                    message = new APISub
                    {
                        content = string.IsNullOrWhiteSpace(queryMessage)
                            ? "支付结果暂时无法确认，请勿重复付款，稍后重新登录查看会员状态"
                            : queryMessage
                    }
                };
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
            // macOS updates are downloaded manually from the official website.
            // Do not open the package URL returned by the version API here.
            await Browser.Default.OpenAsync("http://thankful.top/", BrowserLaunchMode.SystemPreferred);
#endif
        }

        public async Task OpenExternalUrlAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Only HTTP and HTTPS links can be opened.", nameof(url));
            }

            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
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
