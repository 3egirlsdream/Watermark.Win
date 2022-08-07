using JointWatermark.Views;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JointWatermark
{
    public class Global
    {
        public static string logo = "";
        public static string BasePath = AppDomain.CurrentDomain.BaseDirectory;
        public static string sourceImgUrl;
        public static string lastUrl;
        public static char SeparatorChar = System.IO.Path.DirectorySeparatorChar;
        public static string Path_temp;
        public static string Path_output;
        public static string Path_logo;
        public static System.Drawing.Color color = System.Drawing.Color.White;


        public static string mount { get; set; }
        public static string xy { get; set; }
        public static string date { get; set; }
        public static string deviceName { get; set; }

        public static string FontFamily { get; set; } = "FZXiJinLJW";
        public static string FontFamilyLight { get; set; } = "微软雅黑Light";

        public static string Http { get; set; } = "http://thankful.top:4396";

        public static dynamic GetThumbnailPath(string sourceImg, bool showBrand = true)
        {
            using (var bp = SixLabors.ImageSharp.Image.Load(sourceImg))
            {
                var profile = bp.Metadata.ExifProfile?.Values;
                var meta = new Dictionary<string, object>();
                if (profile != null)
                {
                    var meta_origin = profile.Select(x => new
                    {
                        Key = x.Tag.ToString(),
                        Value = x.GetValue() is ushort[]? ((ushort[])x.GetValue())[0] : x.GetValue()
                    });
                   
                    foreach(var item in meta_origin)
                    {
                        meta[item.Key] = item.Value;  
                    }
                    if (meta.ContainsKey("ExposureProgram"))
                    {
                        meta["ExposureProgram"] = ExposureProgram[Convert.ToInt32(meta["ExposureProgram"])];
                    }
                }

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
                        left2,
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
                    left2,
                };
            }
        }

        public static dynamic GetMetaInfo(string sourceImg)
        {
            using (var bp = SixLabors.ImageSharp.Image.Load(sourceImg))
            {
                var profile = bp.Metadata.ExifProfile?.Values;
                var meta = new Dictionary<string, object>();
                if (profile != null)
                {
                    var meta_origin = profile.Select(x => new
                    {
                        Key = x.Tag.ToString(),
                        Value = x.GetValue() is ushort[]? ((ushort[])x.GetValue())[0] : x.GetValue()
                    });

                    foreach (var item in meta_origin)
                    {
                        meta[item.Key] = item.Value;
                    }
                }


                var config = GetDefaultExifConfig(meta);

                var right1 = config[2];
                var right2 = config[3];
                var left1 = config[0];
                var left2 = config[1];

                return new
                {
                    right1,
                    left1,
                    right2,
                    left2,
                };
            }
        }

        public static Dictionary<int, string> ExposureProgram = new Dictionary<int, string>()
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
            var txt = File.ReadAllText(BasePath + SeparatorChar + "Resources/ExifConfig.json");
            if (txt == null) return new List<string>();
            var model = Newtonsoft.Json.JsonConvert.DeserializeObject<MainModel>(txt);
            if (model == null) return new List<string>();
            var ls = new List<string>();

            foreach (var parent in model.Config)
            {
                var cs = new List<string>();
                foreach(var child in parent.Config)
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

    }
}
