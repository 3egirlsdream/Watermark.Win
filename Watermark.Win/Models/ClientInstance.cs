using Masa.Blazor;
using Masa.Blazor.Presets;
using Microsoft.JSInterop;
using MudBlazor;
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

        public Task DownloadTemplate(string watermarkId, ViewParameter parameter, Masa.Blazor.IPopupService PopupService, List<WMZipedTemplate> ZipedTemplates, IWMWatermarkHelper helper, IJSRuntime JSRuntime, Dictionary<string, int> Versions, PageStackNavController NavController, FailedBox failedBox)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<string>> PickMultipleAsync()
        {
            Microsoft.Win32.OpenFileDialog dialog = new()
            {
                DefaultExt = ".png",  // 设置默认类型
                Multiselect = true,                             // 设置可选格式
                Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
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
                Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
            };
            // 打开选择框选择
            var result = dialog.ShowDialog();
            return Task.Run(() =>
            {
                if (result == true) return dialog.FileName;
                return "";
            });
        }

        public Version GetVersion()
        {
            throw new NotImplementedException();
        }

        public Task<bool> CheckUpdate(string platform = "")
        {
            throw new NotImplementedException();
        }

        public Task SetTextAsync(string uri)
        {
            System.Windows.Clipboard.SetText(uri);
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
            throw new NotImplementedException();
        }

        public Task<WMDesignFunc> GetWMDesignFunc(string canvasId)
        {
            throw new NotImplementedException();
        }

        public Task Update(Action<long, long> DownloadProgressChanged)
        {
            throw new NotImplementedException();
        }

        public string UpdateMessage { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string UpdateVersion { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string LinkPath { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }
}
