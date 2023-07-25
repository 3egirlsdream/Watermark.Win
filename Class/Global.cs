using JointWatermark.Class;
using JointWatermark.Enums;
using JointWatermark.Views;
using MaterialDesignThemes.Wpf;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WeakToys.Class;
using Image = SixLabors.ImageSharp.Image;

namespace JointWatermark
{
    public class Global
    {
        public static string BasePath { get; set; } = AppDomain.CurrentDomain.BaseDirectory;
        public static char SeparatorChar { get; set; } = System.IO.Path.DirectorySeparatorChar;
        public static string Path_temp { get; set; }
        public static string Path_output { get; set; }
        public static string Path_logo { get; set; } = BasePath + $"{SeparatorChar}logo";
        public static string Http { get; set; } = "http://thankful.top:4396";



        public static dynamic GetThumbnailPath(string sourceImg)
        {
            using (var bp = SixLabors.ImageSharp.Image.Load(sourceImg))
            {
                var right1 = "";// config[2];
                var right2 = "";// config[3];
                var left1 = "";// config[0];
                var left2 = "";// config[1];

                if (bp.Width <= 1920 || bp.Height <= 1080)
                {
                    return new
                    {
                        path = sourceImg,
                        right1,
                        left1,
                        right2,
                        left2
                    }; ;
                }
                var xs = bp.Width / 1920M;

                var w = (int)(bp.Width / xs);
                var h = (int)(bp.Height / xs);
                var p = Global.Path_temp + Global.SeparatorChar + sourceImg.Substring(sourceImg.LastIndexOf('\\') + 1);
                bp.Mutate(x => x.Resize(w, h));
                try
                {
                    bp.SaveAsJpeg(p);
                }
                catch { }
                return new
                {
                    path = p,
                    right1,
                    left1,
                    right2,
                    left2
                };
            }
        }

        public static Dictionary<string, object> GetMeta(string path, out bool empty)
        {
            var meta = new Dictionary<string, object>();
            using (var img = Image.Load(path))
            {
                if (img.Metadata != null && img.Metadata.ExifProfile != null && img.Metadata.ExifProfile.Values != null)
                {
                    empty = false;
                    meta = GetMeta(img.Metadata.ExifProfile.Values);


                    var xs = img.Width / 1920M;

                    var w = (int)(img.Width / xs);
                    var h = (int)(img.Height / xs);
                    var p = Global.Path_temp + Global.SeparatorChar + path.Substring(path.LastIndexOf('\\') + 1);
                    img.Mutate(x => x.Resize(w, h));
                    try
                    {
                        meta["thumbnail"] = p;
                        img.SaveAsJpeg(p);
                    }
                    catch { }
                }
                else
                {
                    empty = true;
                }
            }
            return meta;
        }

        public static Dictionary<string, object> GetMeta(IReadOnlyList<IExifValue> profile)
        {
            var meta = new Dictionary<string, object>();
            if (profile != null)
            {
                var meta_origin = profile.Select(x => new
                {
                    Key = x.Tag.ToString(),
                    Value = x.GetValue() is ushort[] v ? v[0] : (x.Tag.ToString().Contains("DateTime") ? ToDateTime((string)x.GetValue()) : x.GetValue())
                });

                foreach (var item in meta_origin)
                {
                    var key = item.Key;
                    var value = item.Value;
                    if (value is Rational et && et.Denominator != 0 && et.Numerator != 0)
                    {
                        if (et.Denominator % et.Numerator == 0)
                        {
                            value = "1/" + (int)(et.Denominator / et.Numerator);
                        }
                        else
                        {
                            value = et.Numerator * 1.0 / et.Denominator;
                        }
                    }
                    else if (value is Rational[] lr)
                    {
                        var rtl = "";
                        bool over0 = false;
                        if (key == "GPSLatitude")
                        {
                            DealLatitudeLongitude(lr, ref rtl, ref over0);
                            rtl += (over0 ? "N" : "S");
                        }
                        else if (key == "GPSLongitude")
                        {
                            DealLatitudeLongitude(lr, ref rtl, ref over0);
                            rtl += (over0 ? "E" : "W");
                        }
                        value = rtl;
                    }
                    else if (key == "ExposureProgram")
                    {
                        value = ExposureProgram[Convert.ToInt32(value)];
                    }

                    meta[key] = value;
                }
            }

            return meta;
        }

        private static void DealLatitudeLongitude(Rational[] lr2, ref string rtl, ref bool over0)
        {
            for (var i = 0; i < lr2.Length; i++)
            {
                var item = lr2[i];
                var r = item.Denominator == 0 ? 0 : item.Numerator / item.Denominator;
                if (i == 0)
                {
                    over0 = r > 0;
                }
                rtl += r;
                if (i == 0) rtl += "°";
                else if (i == 1) rtl += "'";
                else if (i == 2) rtl += "''";
            }
        }

        //2021:10:24 17:04:49
        private static DateTime ToDateTime(string s)
        {
            try
            {
                var ls = s.Split(new string[] { ":", ".", "/", "\\", " " }, StringSplitOptions.RemoveEmptyEntries);
                var year = Convert.ToInt16(ls[0]);
                var month = Convert.ToInt16(ls[1]);
                var day = Convert.ToInt16(ls[2]);
                var hour = Convert.ToInt16(ls[3]);
                var minute = Convert.ToInt16(ls[4]);
                var second = Convert.ToInt16(ls[5]);
                var dt = new DateTime(year, month, day, hour, minute, second);
                return dt;
            }
            catch
            {
                return DateTime.Now;
            }
        }

        public static Dictionary<int, string> ExposureProgram { get; set; } = new Dictionary<int, string>()
        {
            {0, "未知" },
            {1, "手动" },
            {2, "正常" },
            {3, "光圈优先" },
            {4, "快门优先" },
            {5, "创作程序(偏重使用视野深度)" },
            {6, "操作程序(偏重使用快门速度)" },
            {7, "纵向模式" },
            {8, "横向模式" },
        };

        public static List<string> GetDefaultExifConfig(Dictionary<string, object> meta)
        {
            var model = InitConfig();
            if (model == null) return new List<string>();
            var ls = new List<string>();

            foreach (var parent in model.Config)
            {
                var cs = new List<string>();
                foreach (var child in parent.Config)
                {
                    if (meta.TryGetValue(child.Key, out object rtl))
                    {
                        var c = child.Front + rtl + child.Behind;
                        cs.Add(c);
                    }
                    else
                    {
                        var c = child.Front + child.Value + child.Behind;
                        cs.Add(c);
                    }
                }

                var p = string.Join(" ", cs);
                ls.Add(p);
            }

            return ls;
        }

        public static string GetExifInfo(Dictionary<string, object> meta, List<ExifConfigInfo> info, List<string> dateFormat = null)
        {
            var cs = new List<string>();
            dateFormat ??= new List<string>() { ".", ".", ":", ":" };
            if (info == null) return "";
            foreach (var child in info)
            {
                if (meta.TryGetValue(child.Key, out object rtl))
                {

                    var c = child.Front + GetDateTimeFormat(dateFormat, rtl) + child.Behind;
                    cs.Add(c);
                }
                else
                {
                    var c = child.Front + child.Value + child.Behind;
                    cs.Add(c);
                }
            }

            var p = string.Join(" ", cs);

            return p;
        }

        public static object GetDateTimeFormat(List<string> dateFormat, object rtl)
        {
            dateFormat ??= new List<string>() { ".", ".", ":", ":" };
            return rtl is DateTime time ? time.ToString($"yyyy{dateFormat[0]}MM{dateFormat[1]}dd HH{dateFormat[2]}mm{dateFormat[3]}ss") : rtl;
        }

        public static MainModel InitConfig()
        {
            Stream ms;
            var path = Global.BasePath + Global.SeparatorChar + "ExifConfig.json";
            if (File.Exists(path))
            {
                ms = new FileStream(path, FileMode.Open);
            }
            else
            {
                ms = new MemoryStream(Properties.Resources.ExifConfig);
            }
            using (var reader = new StreamReader(ms))
            {
                var c = reader.ReadToEnd();
                var result = JsonConvert.DeserializeObject<MainModel>(c);
                ms.Dispose();
                return result ?? new MainModel();
            }
        }

        public static string GetUpdateLog()
        {
            return Properties.Resources.UpdateLog;
        }

        public static Dictionary<string, byte[]> FontResourrce { get; set; } = new Dictionary<string, byte[]>
        {
            { "OpenSans", Properties.Resources.OpenSans },
            { "金陵宋体", Properties.Resources.FZXiJinLJW },
            { "Pamega", Properties.Resources.Pamega_demo_2 },
            { "Hey-November", Properties.Resources.Hey_November_2},
            { "Facon", Properties.Resources.Facon_2 }
        };

        public static Dictionary<string, byte[]> SystemImage { get; set; } = new Dictionary<string, byte[]>
        {
            { "system_t", Properties.Resources.system_t },
            { "system_b", Properties.Resources.system_b }
        };


        public static bool SaveConfig(string json)
        {
            try
            {
                File.WriteAllText(Global.BasePath + Global.SeparatorChar + "ExifConfig.json", json);
            }
            catch
            {
                return false;
            }
            return true;
        }

        public static void SendMsg(string msg)
        {
            var error = new Error(msg);
            if (App.Current.MainWindow.Activate())
            {
                error.Owner = App.Current.MainWindow;
            }
            error.ShowInTaskbar = false;
            error.ShowDialog();
        }

        public static string Resolution { get; set; } = "default";
        public static bool ClearMeta { get; set; } = false;
        public static int Quality { get; set; } = 100;

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


        public static List<string> InitFontList()
        {
            var fonts = Global.FontResourrce.Select(c => c.Key).ToList();
            var path = Global.BasePath + Global.SeparatorChar + "fonts";
            if (Directory.Exists(path))
            {
                var files = new DirectoryInfo(path);
                foreach (var item in files.GetFiles())
                {
                    if (!item.Name.ToLower().Contains("bold"))
                    {
                        var ls = item.Name.Split('/').Last().Split('.')[0];
                        fonts.Add(ls);
                    }
                }
            }
            return fonts;
        }

        public static string GetContent(GeneralWatermarkRowProperty row, Dictionary<string, object> meta)
        {
            if (row.DataSource.From == DataSourceFrom.Exif)
            {
                var rst = GetExifInfo(meta, row.DataSource.Exifs, row.DateFormat);
                return rst;
            }
            else return row.Content;
        }
        public static GeneralWatermarkProperty Init()
        {
            //GeneralWatermarkProperty image;
            //image = new GeneralWatermarkProperty();
            //image.PhotoPath = "C:\\Users\\Jiang\\Pictures\\bb.jpg";
            //image.StartPosition = new SixLabors.ImageSharp.Point(0, 15);
            //image.PecentOfHeight = 70;
            //image.PecentOfWidth = 100;
            //image.EnableFixedPercent = true;
            //image.Shadow = new ImageShadow(false, 200);
            //image.ImageBackgroud = new ImageBackgroud()
            //{
            //    Type = ImageBackgroudType.Image,
            //    Top = "system_t",
            //    Bottom = "system_b"
            //};
            //image.Properties = new List<GeneralWatermarkRowProperty>();
            //image.ConnectionModes = new List<ConnectionMode>();
            var image = Global.InitConfig().Templates?.PhotoFrame;
            image.PecentOfHeight = 100;
            image.PecentOfWidth = 100;
            image.StartPosition = new SixLabors.ImageSharp.Point(0, 0);
            image.EnableFixedPercent = true;
            image.Shadow = new ImageShadow(false, 200);
            //image.PhotoPath = "C:\\Users\\kingdee\\Pictures\\Camera Roll\\Windows10.jpg";
            //image.Properties[2].ImagePath = "C:\\Users\\kingdee\\Downloads\\苹果.png";
            return image;
        }

        public delegate void RefreshLogoDel();
        public static RefreshLogoDel RefreshLogoAction { get; set; }

        public static Action<Photo> ApplyAllImages;
        public static Action SuggestAction;
        public static string CurrentTemplate { get; set; }

        public static async void CheckUpdate(string nowv, Action<string, string> action)
        {
            var version = await Connections.HttpGetAsync<CLIENT_VERSION>(Global.Http + "/api/CloudSync/GetVersion?Client=Watermark", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.VERSION != null)
            {
                var v1 = new Version(nowv);
                var v2 = new Version(version.data.VERSION);
                if (v2 > v1)
                    action.Invoke($"有新版本V{version.data.VERSION}可以下载", version.data.MEMO);
            }
        }
    }

}

