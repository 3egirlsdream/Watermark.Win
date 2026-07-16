using Masa.Blazor;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Watermark.Win;
using Watermark.Win.Models;
using Watermark.Win.Views;

namespace Watermark.Shared.Models
{
    public class ClientInstance : IClientInstance
    {
        private string UUID()
        {
            string code = null;
            SelectQuery query = new SelectQuery("select * from Win32_ComputerSystemProduct");
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
            {
                foreach (var item in searcher.Get())
                {
                    using (item) code = item["UUID"].ToString();
                }
            }
            return code;
        }

        public string Key()
        {
            var result = Convert.ToBase64String(Encoding.UTF8.GetBytes(UUID().Replace("-", "") + "CATLNMSL"));
            string result3 = result.Replace("-", "");
            return result3;
        }

        public void Haptic()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<string>> PickMultipleAsync()
        {
            Microsoft.Win32.OpenFileDialog dialog = new()
            {
                DefaultExt = ".png",  // 设置默认类型
                Multiselect = true,                             // 设置可选格式
                Filter = PhotoFilter
            };
            // 打开选择框选择
            var result = dialog.ShowDialog();
            return Task.Run(() =>
            {
                if (result == true) return dialog.FileNames.AsEnumerable();
                return [];
            });
        }

        public Task<string> PickAsync()
        {
            Microsoft.Win32.OpenFileDialog dialog = new()
            {
                DefaultExt = ".png",  // 设置默认类型
                Multiselect = false,                             // 设置可选格式
                Filter = PhotoFilter
            };
            // 打开选择框选择
            var result = dialog.ShowDialog();
            return Task.Run(() =>
            {
                if (result == true) return dialog.FileName;
                return "";
            });
        }

        private const string PhotoFilter = "照片与 RAW|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.tif;*.tiff;*.dng;*.cr2;*.cr3;*.nef;*.nrw;*.arw;*.sr2;*.raf;*.orf;*.rw2;*.rwl;*.pef;*.3fr;*.iiq;*.srw|普通照片|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.tif;*.tiff|RAW 照片|*.dng;*.cr2;*.cr3;*.nef;*.nrw;*.arw;*.sr2;*.raf;*.orf;*.rw2;*.rwl;*.pef;*.3fr;*.iiq;*.srw";

        public Version GetVersion()
        {
            return typeof(ClientInstance).Assembly.GetName().Version ?? new Version(1, 0);
        }

        public async Task<bool> CheckUpdate(string platform = "")
        {
            if (string.IsNullOrWhiteSpace(platform)
                || string.Equals(platform, "WatermarkAndroid", StringComparison.OrdinalIgnoreCase))
                platform = "WatermarkV3";
            try
            {
                var version = await Connections.HttpGetAsync<WMClientVersion>(
                    APIHelper.HOST + $"/api/CloudSync/GetVersion?Client={Uri.EscapeDataString(platform)}",
                    Encoding.Default);
                if (version?.success != true || string.IsNullOrWhiteSpace(version.data?.VERSION)) return false;
                UpdateMessage = version.data.MEMO ?? string.Empty;
                UpdateVersion = version.data.VERSION;
                LinkPath = version.data.PATH ?? string.Empty;
                return new Version(UpdateVersion) > GetVersion();
            }
            catch { return false; }
        }

        public Task SetTextAsync(string uri)
        {
            System.Windows.Clipboard.SetText(uri);
            return Task.CompletedTask;
        }

        public Task OpenExternalUrlAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
                return Task.CompletedTask;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            return Task.CompletedTask;
        }

        public Task<API<string>> AliPays(decimal cost, string tradeName)
        {
            throw new NotImplementedException();
        }

        public Task ReLogin()
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsOutOfDate(string client = "Watermark_A")
        {
            throw new NotImplementedException();
        }

        public bool Save(byte[] b64, string fn)
        {
            throw new NotImplementedException();
        }

        public Task<string> OpenFolder()
        {
            Microsoft.Win32.OpenFolderDialog dialog = new();
            var result = dialog.ShowDialog();
            return Task.Run(() =>
            {
                if (result == true)
                {
                    return dialog.FolderName;
                }
                return "";
            });
        }

        public void SetColor(string color = "#F5F5F5")
        {
        }

        public Task<WMDesignFunc> GetWMDesignFunc(string canvasId)
        {
            throw new NotImplementedException();
        }

        public async Task Update(Action<long, long> DownloadProgressChanged)
        {
            if (!string.IsNullOrWhiteSpace(LinkPath)) await OpenExternalUrlAsync(LinkPath);
        }

        public Task<bool> Download(string directory, string fileName, string extension)
        {
            throw new NotImplementedException();
        }

        public void Exit()
        {
            System.Windows.Application.Current.Shutdown();
        }

        public void OpenDesign(WMCanvas canvas)
        {
            throw new NotImplementedException();
        }

        public Task InteropInit(string appId)
        {
            throw new NotImplementedException();
        }

        public string UpdateMessage { get; set; } = string.Empty;
        public string UpdateVersion { get; set; } = string.Empty;
        public string LinkPath { get; set; } = string.Empty;
        public string AppTitle { get; set; } = App.Current.MainWindow.Title;
        public void WindowMinimize() { }
        public void WindowZoom() { }
        public void WindowClose() { }
        public void WindowStartDrag() { }
        public void WindowDragMove() { }
        public void WindowEndDrag() { }
    }
}
