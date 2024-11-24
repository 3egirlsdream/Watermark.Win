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

        public Action<WMCanvas, WMLogo, ConcurrentDictionary<string, byte[]>> SelectImageAction = (canvas, mLogo, ImagesBase64) =>
        {
            Microsoft.Win32.OpenFileDialog dialog = new()
            {
                DefaultExt = ".png",  // 设置默认类型
                Multiselect = false,                             // 设置可选格式
                Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
            };
            // 打开选择框选择
            Nullable<bool> result = dialog.ShowDialog();
            if (result == true)
            {
                var p = dialog.FileName;
                var destFolder = Global.AppPath.TemplatesFolder + canvas.ID;
                if (!System.IO.Directory.Exists(destFolder))
                {
                    System.IO.Directory.CreateDirectory(destFolder);
                }

                var name = p.Substring(p.LastIndexOf('\\') + 1);
                var destFile = destFolder + System.IO.Path.DirectorySeparatorChar + name;
                System.IO.File.Copy(p, destFile, true);
                mLogo.Path = name;
                Global.ImageFile2Base64(ImagesBase64, destFile, mLogo.ID);
            }
        };

        public Action<WMCanvas, WMContainer, ConcurrentDictionary<string, byte[]>> SelectContainerImageAction = (canvas, mContainer, ImagesBase64) =>
        {
            Microsoft.Win32.OpenFileDialog dialog = new()
            {
                DefaultExt = ".png",  // 设置默认类型
                Multiselect = false,                             // 设置可选格式
                Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
            };
            // 打开选择框选择
            Nullable<bool> result = dialog.ShowDialog();
            if (result == true)
            {
                var p = dialog.FileName;
                var destFolder = Global.AppPath.TemplatesFolder + canvas.ID;
                if (!System.IO.Directory.Exists(destFolder))
                {
                    System.IO.Directory.CreateDirectory(destFolder);
                }

                var name = Path.GetFileName(p);
				var destFile = destFolder + System.IO.Path.DirectorySeparatorChar + name;
				using var bitmap = SKBitmap.Decode(p);
				WriteThumbnailImage(bitmap, destFile);
                mContainer.Path = name;
                Global.ImageFile2Base64(ImagesBase64, destFile, mContainer.ID);
            }
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


        public Action<List<string>> ImportLocalFontAction = (Fonts) =>
        {
            var fontPath = AppDomain.CurrentDomain.BaseDirectory + "fonts";
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.DefaultExt = ".ttf";  // 设置默认类型
            dialog.Multiselect = false;                             // 设置可选格式
            dialog.Filter = @"字体文件(*.ttf,*.otf)|*ttf;*.otf";
            // 打开选择框选择
            Nullable<bool> result = dialog.ShowDialog();
            if (result == true)
            {
                var f = dialog.FileName;
                if (!Directory.Exists(fontPath))
                {
                    Directory.CreateDirectory(fontPath);
                }
                var file = new FileInfo(f);
                if (file.Exists)
                {
                    try
                    {
                        var target = fontPath + Path.DirectorySeparatorChar + Path.GetFileName(f);
                        file.CopyTo(target, true);
                    }
                    catch { }
                    finally
                    {
                        InitLocalFontsAction.Invoke(Fonts);
                    }
                }

            }
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

        public void SelectDefaultImage(string id, ConcurrentDictionary<string, byte[]> dic)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog dialog = new()
                {
                    DefaultExt = ".png",  // 设置默认类型
                    Multiselect = false,                             // 设置可选格式
                    Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
                };
                // 打开选择框选择
                Nullable<bool> result = dialog.ShowDialog();

                if (result == true)
                {
                    var p = dialog.FileName;
                    var destFolder = Global.AppPath.TemplatesFolder + id;
                    if (!System.IO.Directory.Exists(destFolder))
                    {
                        System.IO.Directory.CreateDirectory(destFolder);
                    }

                    var name = p.Substring(p.LastIndexOf('\\') + 1);
                    var destFile = destFolder + System.IO.Path.DirectorySeparatorChar + "default.jpg";

                    Global.ImageFile2Base64(dic, p, "default");
                    SkiaSharp.SKBitmap bitmap = SkiaSharp.SKBitmap.Decode(p);
                    WriteThumbnailImage(bitmap, destFile);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public static void WriteThumbnailImage(SKBitmap source, string target)
        {
            double w = source.Width, h = source.Height;
            var xs = 1080.0 / h;
            var resized = source.Resize(new SkiaSharp.SKImageInfo((int)(w * xs), (int)(h * xs)), SkiaSharp.SKFilterQuality.High);
            using var image = SKImage.FromBitmap(resized);
            using var writeStream = File.OpenWrite(target);
			SKEncodedImageFormat format = Path.GetExtension(target)?.ToLower() == ".png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
			image.Encode(format, 50).SaveTo(writeStream);
        }

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

        public void OpenSetting()
        {
            var action = new Action(() =>
            {
                var setting = new Setting();
                setting.Owner = Application.Current.MainWindow;
                setting.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                setting.ShowInTaskbar = false;
                setting.ShowDialog();
            });
            OpenWinHelper.Open(action);
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

        public void SetColor()
        {
            throw new NotImplementedException();
        }

        public Func<Task<string>> CreateNewTemplate = new Func<Task<string>>(() =>
        {
            Microsoft.Win32.OpenFileDialog dialog = new()
            {
                DefaultExt = ".png",  // 设置默认类型
                Multiselect = false,                             // 设置可选格式
                Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
            };
            // 打开选择框选择
            var result = dialog.ShowDialog();
            string v = "";
            if (result == true)
            {
                v = dialog.FileName;
            }
            return Task.Run(() => v);
        });

        public string UpdateMessage { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string UpdateVersion { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string LinkPath { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }
}
