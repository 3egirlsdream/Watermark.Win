using JointWatermark.Class;
using JointWatermark.Enums;
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
                var profile = bp.Metadata.ExifProfile?.Values;
                Dictionary<string, object> meta = GetMeta(profile);

                var config = GetDefaultExifConfig(meta);

                var right1 = config[2];
                var right2 = config[3];
                var left1 = config[0];
                var left2 = config[1];

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

        public static Dictionary<string, object> GetMeta(string path)
        {
            var meta = new Dictionary<string, object>();
            using (var img = Image.Load(path))
            {
                if (img.Metadata != null && img.Metadata.ExifProfile != null && img.Metadata.ExifProfile.Values != null)
                {
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
                    Value = x.GetValue() is ushort[]? ((ushort[])x.GetValue())[0] : (x.Tag.ToString().Contains("DateTime") ? Convert.ToDateTime(ToDateTime((string)x.GetValue())) : x.GetValue())
                });

                foreach (var item in meta_origin)
                {
                    meta[item.Key] = item.Value;
                }
                if (meta.ContainsKey("ExposureProgram"))
                {
                    meta["ExposureProgram"] = ExposureProgram[Convert.ToInt32(meta["ExposureProgram"])];
                }

                if (meta.ContainsKey("FNumber") && meta["FNumber"] is SixLabors.ImageSharp.Rational rational && rational.Denominator != 0)
                {
                    meta["FNumber"] = rational.Numerator * 1.0 / rational.Denominator;
                }

                if(meta.TryGetValue("GPSLatitude", out object val) && val is SixLabors.ImageSharp.Rational[] lr)
                {
                    var rtl = "";
                    bool over0 = false;
                    DealLatitudeLongitude(lr, ref rtl, ref over0);
                    meta["GPSLatitude"] = rtl + (over0 ? "N" : "S");
                }

                if (meta.TryGetValue("GPSLongitude", out object val2) && val2 is SixLabors.ImageSharp.Rational[] lr2)
                {
                    var rtl = "";
                    bool over0 = false;
                    DealLatitudeLongitude(lr2, ref rtl, ref over0);
                    meta["GPSLongitude"] = rtl + (over0 ? "E" : "W");
                }
            }

            return meta;
        }

        private static void DealLatitudeLongitude(Rational[] lr2, ref string rtl, ref bool over0)
        {
            for (var i = 0; i < lr2.Length; i++)
            {
                var item = lr2[i];
                var r = item.Numerator / item.Denominator;
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
        private static string ToDateTime(string s)
        {
            try
            {
                var ls = s.Split(new string[] {":", ".", "/", "\\", " "}, StringSplitOptions.RemoveEmptyEntries);
                var year = Convert.ToInt16(ls[0]);
                var month = Convert.ToInt16(ls[1]);
                var day = Convert.ToInt16(ls[2]);
                var hour = Convert.ToInt16(ls[3]);
                var minute = Convert.ToInt16(ls[4]);
                var second = Convert.ToInt16(ls[5]);
                var dt = new DateTime(year, month, day, hour, minute, second);
                return dt.ToString();
            }
            catch
            {
                return s;
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
            if (dateFormat == null)
            {
                dateFormat = new List<string>() { ".", ".", ":", ":" };
            }
            if (info == null) return "";
            foreach (var child in info)
            {
                if (meta.TryGetValue(child.Key, out object rtl))
                {

                    var c = child.Front + (rtl is DateTime ? Convert.ToDateTime(rtl).ToString($"yyyy{dateFormat[0]}MM{dateFormat[1]}dd HH{dateFormat[2]}mm{dateFormat[3]}ss") : rtl) + child.Behind;
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

        public static Dictionary<string, byte[]> FontResourrce { get; set; } = new Dictionary<string, byte[]>
        {
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
            ((MainWindow)App.Current.MainWindow).SendMsg(msg);
        }

        public static string Resolution { get; set; } = "default";

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
            fonts.Insert(0, "微软雅黑");
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
            GeneralWatermarkProperty image;
            image = new GeneralWatermarkProperty();
            image.PhotoPath = "C:\\Users\\Jiang\\Pictures\\bb.jpg";
            image.StartPosition = new SixLabors.ImageSharp.Point(0, 15);
            image.PecentOfHeight = 70;
            image.PecentOfWidth = 100;
            image.EnableFixedPercent = true;
            image.Shadow = new ImageShadow(false, 200);
            image.ImageBackgroud = new ImageBackgroud()
            {
                Type = ImageBackgroudType.Image,
                Top = "system_t",
                Bottom = "system_b"
            };
            image.Properties = new List<GeneralWatermarkRowProperty>();
            image.ConnectionModes = new List<ConnectionMode>();
            //image = Global.InitConfig().Templates?.PhotoFrame;
            //image.PhotoPath = "C:\\Users\\kingdee\\Pictures\\Camera Roll\\Windows10.jpg";
            //image.Properties[2].ImagePath = "C:\\Users\\kingdee\\Downloads\\苹果.png";
            return image;
        }

        public delegate void RefreshLogoDel();
        public static RefreshLogoDel RefreshLogoAction { get; set; }

        public static Action<Photo> ApplyAllImages;

    }
}
