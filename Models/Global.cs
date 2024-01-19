
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Watermark.Win.Views;

namespace Watermark.Win.Models
{
    public static class Global
    {
        public static string TemplatesFolder = AppDomain.CurrentDomain.BaseDirectory + "Templates" + System.IO.Path.DirectorySeparatorChar;
        public static string ThumbnailFolder = AppDomain.CurrentDomain.BaseDirectory + "Thumbnails" + System.IO.Path.DirectorySeparatorChar;
        public static LoginChildModel CurrentUser = new LoginChildModel();

        public static WMCanvas ReadConfigFromPath(string path)
        {
            using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open);
            using var reader = new System.IO.StreamReader(stream);
            var content = reader.ReadToEnd();
            return ReadConfig(content);
        }

        public static WMCanvas ReadConfig(string json)
        {
            try
            {
                var ls = Newtonsoft.Json.JsonConvert.DeserializeObject<WMCanvasSerialize>(json);
                if(ls == null) return new WMCanvas();
                var newCanvas = new WMCanvas
                {
                    ID = ls.ID,
                    Name = ls.Name,
                    BorderThickness = ls.BorderThickness,
                    BackgroundColor =  ls.BackgroundColor,
                    ImageProperties = ls.ImageProperties,
                    EnableMarginXS = ls.EnableMarginXS,
                    //一级容器
                    Children = new List<WMContainer>(ls.Containers.Where(c => c.PNode.PID == "0"))
                };
                //每个容器下的二级节点
                foreach (var container in newCanvas.Children)
                {
                    container.Controls ??= [];
                    container.Controls.AddRange(ls.Lines.Where(c => c.PNode.PID == container.ID));
                    container.Controls.AddRange(ls.Logos.Where(c => c.PNode.PID == container.ID));
                    container.Controls.AddRange(ls.Texts.Where(c => c.PNode.PID == container.ID));

                    var secondContainer = ls.Containers.Where(c => c.PNode.PID == container.ID);
                    foreach (var ctrl in secondContainer)
                    {
                        ctrl.Controls ??= [];
                        ctrl.Controls.AddRange(ls.Lines.Where(c => c.PNode.PID == ctrl.ID));
                        ctrl.Controls.AddRange(ls.Logos.Where(c => c.PNode.PID == ctrl.ID));
                        ctrl.Controls.AddRange(ls.Texts.Where(c => c.PNode.PID == ctrl.ID));
                    }
                    secondContainer = secondContainer.OrderBy(c => c.PNode.SEQ);
                    container.Controls.AddRange(secondContainer);
                    container.Controls = new List<IWMControl>(container.Controls.OrderBy(c => c.PNode.SEQ));
                }
                newCanvas.Children = new List<WMContainer>(newCanvas.Children.OrderBy(c => c.PNode.SEQ));
                return newCanvas;
            }
            catch(Exception ex)
            {
                return new WMCanvas();
            }
        }

        public static string CanvasSerialize(WMCanvas canvas)
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                MissingMemberHandling = MissingMemberHandling.Ignore // 忽略缺少的字段
            };
            var nc = JsonConvert.DeserializeObject<WMCanvasSerialize>(JsonConvert.SerializeObject(canvas), settings);
            nc ??= new WMCanvasSerialize();
            nc.Containers = [];
            nc.Lines = [];
            nc.Logos = [];
            nc.Texts = [];


            for (var i = 0; i < canvas.Children.Count; i++)
            {
                var ct = canvas.Children[i];
                ct.PNode = new PNode(i, "0");
                for (var j = 0; j < ct.Controls.Count; j++)
                {
                    var mc = ct.Controls[j];
                    mc.PNode = new PNode(j, ct.ID);
                    if (mc is WMLine mLine) nc.Lines.Add(mLine);
                    else if (mc is WMLogo mLogo) nc.Logos.Add(mLogo);
                    else if (mc is WMText mText) nc.Texts.Add(mText);
                    else if (mc is WMContainer mContainer)
                    {
                        for (var k = 0; k < mContainer.Controls.Count; k++)
                        {
                            var child = mContainer.Controls[k];
                            child.PNode = new PNode(k, mContainer.ID);
                            if (child is WMLine line) nc.Lines.Add(line);
                            else if (child is WMLogo logo) nc.Logos.Add(logo);
                            else if (child is WMText text) nc.Texts.Add(text);
                        }
                        var cld_copy = JsonConvert.DeserializeObject<WMContainer>(JsonConvert.SerializeObject(mContainer))??new WMContainer();
                        cld_copy.Controls = [];
                        nc.Containers.Add(cld_copy);
                    }
                }
                var copy = JsonConvert.DeserializeObject<WMContainer>(JsonConvert.SerializeObject(ct))??new WMContainer();
                copy.Controls = [];
                nc.Containers.Add(copy);
            }

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(nc, Newtonsoft.Json.Formatting.Indented);
            return json;
        }

        public static void ImageFile2Base64(Dictionary<string, string> ImagesBase64, string destFile, string id)
        {
            if (string.IsNullOrEmpty(destFile))
            {
                ImagesBase64[id] = "";
                return;
            }
            using var bitmap = SkiaSharp.SKBitmap.Decode(destFile);
            if (bitmap != null)
            {
                using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 50);
                ImagesBase64[id] = "data:image/jpeg;base64," + Convert.ToBase64String(data.ToArray());
            }
            else
            {
                ImagesBase64[id] = "";
            }
        }

        public static void SelectDefaultImage(string id, Dictionary<string, string> dic)
        {
            try
            {
                var action = new Action(() =>
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
                        var destFolder = Global.TemplatesFolder + id;
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
                });
                OpenWinHelper.Open(action);
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
            var resized = source.Resize(new SkiaSharp.SKImageInfo((int)(w * xs), (int)(h * xs)), SkiaSharp.SKFilterQuality.Low);
            using var image = SKImage.FromBitmap(resized);
            using var writeStream = File.OpenWrite(target);
            image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80).SaveTo(writeStream);
        }

        public static Task WriteThumbnailImageAsync(SKBitmap source, string target)
        {
            return Task.Run(() =>
            {
                WriteThumbnailImage(source, target);
                return Task.CompletedTask;
            });
        }

        private static string UUID()
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
        public static string Key()
        {
            using (var md5 = MD5.Create())
            {
                var result = md5.ComputeHash(Encoding.UTF8.GetBytes(UUID().Replace("-", "") + "CATLNMSL"));
                var strResult = BitConverter.ToString(result);
                string result3 = strResult.Replace("-", "");
                return result3;
            }
        }

        public static void WriteAccount2Local(string username, string password)
        {
            var path = AppDomain.CurrentDomain.BaseDirectory + $".sys";
            if(!Directory.Exists(path)) Directory.CreateDirectory(path);
            path += $"{Path.DirectorySeparatorChar}sys";
            if(File.Exists(path)) File.Delete(path);
            var str = new
            {
                username, password
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(str));
        }
        public static Task WriteAccount2LocalAsync(string username, string password)
        {
            return Task.Run(() => WriteAccount2Local(username, password));
        }

        public static Tuple<string, string> ReadLocal()
        {
            var path = $"{AppDomain.CurrentDomain.BaseDirectory}.sys{Path.DirectorySeparatorChar}sys";
            if(!File.Exists (path)) return Tuple.Create(string.Empty, string.Empty); 
            using var fs = new FileStream(path, FileMode.Open);
            using var sr = new StreamReader(fs);
            var content = sr.ReadToEnd();
            if (!string.IsNullOrEmpty(content))
            {
                var result = JsonConvert.DeserializeObject<dynamic>(content);
                return Tuple.Create(Convert.ToString(result?.username ?? ""), Convert.ToString(result?.password ?? ""));
            }
            return Tuple.Create(string.Empty, string.Empty);
        }

        public static dynamic ReadSYS()
        {
            var path = $"{AppDomain.CurrentDomain.BaseDirectory}.sys{Path.DirectorySeparatorChar}qiniu.txt";
            if (!File.Exists(path)) return Tuple.Create(string.Empty, string.Empty);
            using var fs = new FileStream(path, FileMode.Open);
            using var sr = new StreamReader(fs);
            var content = sr.ReadToEnd();
            if (!string.IsNullOrEmpty(content))
            {
                var result = JsonConvert.DeserializeObject<dynamic>(content);
                return result;
            }
            return new { };
        }

        public static Task<Tuple<string, string>> ReadLocalAsync()
        {
            return Task.Run(() => ReadLocal());
        }

        public static void OpenSetting()
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
    }
}
