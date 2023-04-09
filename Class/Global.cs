using JointWatermark.Class;
using JointWatermark.Views;
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
            using(var img = Image.Load(path))
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
                    Value = x.GetValue() is ushort[]? ((ushort[])x.GetValue())[0] : x.GetValue()
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
            }

            return meta;
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
            if(dateFormat == null)
            {
                dateFormat = new List<string>() { ".", ".", ":", ":"};
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
            image.StartPosition = new SixLabors.ImageSharp.Point(5, 5);
            image.PecentOfHeight = 82;
            image.PecentOfWidth = 90;
            image.EnableFixedPercent = true;
            image.Shadow = new ImageShadow(true, 200);
            image.Properties = new List<GeneralWatermarkRowProperty>
            {
                new GeneralWatermarkRowProperty()
                {
                    Name = "右侧第一行",
                    X = PositionBase.Right,
                    Y = PositionBase.Center,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    EdgeDistanceCharacterX = "ABCD",
                    EdgeDistanceCharacterY = "AA",
                    Content = "cesiumcesium测试",
                    Start = WatermarkRange.BottomOfPhoto,
                    End = WatermarkRange.End,
                    IsBold = true,
                    FontSize = 35
                },
                new GeneralWatermarkRowProperty()
                {
                    Name = "右侧第二行",
                    X = PositionBase.Right,
                    Y = PositionBase.Center,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    EdgeDistanceCharacterX = "ABCD",
                    EdgeDistanceCharacterY = "AA",
                    Content = "cesiumcesium测试",
                    Start = WatermarkRange.BottomOfPhoto,
                    End = WatermarkRange.End,
                    IsBold = false,
                    Color = "#cbb795"
                },
                new GeneralWatermarkRowProperty()
                {
                    X = PositionBase.Right,
                    Y = PositionBase.Center,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    EdgeDistanceCharacterX = "ABCD",
                    EdgeDistanceCharacterY = "AA",
                    Content = "右侧cesiumcesium测试",
                    Start = WatermarkRange.BottomOfPhoto,
                    End = WatermarkRange.End,
                    IsBold = true,
                    FontSize = 35,
                    ImagePath = new Photo("C:\\Users\\Jiang\\Pictures\\t01a29dac4bb27f7e22.png", false),
                    ImagePercentOfRange = 50,
                    ContentType = ContentType.Image
                },
                new GeneralWatermarkRowProperty()
                {
                    Name = "左侧第一行",
                    X = PositionBase.Left,
                    Y = PositionBase.Center,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    EdgeDistanceCharacterX = "ABCD",
                    EdgeDistanceCharacterY = "AA",
                    Content = "右侧cesiumcesium测试右侧cesiumcesium",
                    Start = WatermarkRange.BottomOfPhoto,
                    End = WatermarkRange.End,
                    IsBold = true
                },
                new GeneralWatermarkRowProperty()
                {
                    Name = "左侧第二行",
                    X = PositionBase.Left,
                    Y = PositionBase.Center,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    EdgeDistanceCharacterX = "AAA",
                    EdgeDistanceCharacterY = "AA",
                    Content = "t01a29dac4bb27f7e22.png",
                    Start = WatermarkRange.BottomOfPhoto,
                    End = WatermarkRange.End,
                    ContentType = ContentType.Text,
                    RelativePositionMode = RelativePositionMode.LastRow,
                    Color = "#cbb795"
                },
                new GeneralWatermarkRowProperty()
                {
                    X = PositionBase.Right,
                    Y = PositionBase.Center,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    LinePercentOfRange = 60,
                    LinePixel = 2,
                    Start = WatermarkRange.BottomOfPhoto,
                    End = WatermarkRange.End,
                    ContentType = ContentType.Line,
                    Color = "#e6e6e6",
                    RelativePositionMode = RelativePositionMode.LastRow
                },

            };

            image.ConnectionModes = new List<ConnectionMode>()
            {
                new ConnectionMode
                {
                    Ids = new List<string>(image.Properties.Select(c=>c.ID).Take(2)),
                    RowHeightMinFontPercent = 30,
                    X = PositionBase.Right,
                    Y = PositionBase.Center,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    EdgeDistanceCharacterX = "ABCD",
                    EdgeDistanceCharacterY = "AA",
                    Start = WatermarkRange.BottomOfPhoto,
                    End = WatermarkRange.End
                },
                new ConnectionMode
                {
                    Ids = new List<string>(){image.Properties[5].ID },
                    LinePixel = 2,
                    X = PositionBase.Right,
                    Y = PositionBase.Center,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    EdgeDistanceCharacterX = "a",
                    EdgeDistanceCharacterY = "a",
                    Start = WatermarkRange.BottomOfPhoto,
                    End = WatermarkRange.End,
                    RelativePositionMode = RelativePositionMode.LastRow
                },
                new ConnectionMode
                {
                    Ids = new List<string>(image.Properties.Select(c=>c.ID).Skip(2).Take(1)),
                    RowHeightMinFontPercent = 30,
                    X = PositionBase.Right,
                    Y = PositionBase.Center,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    EdgeDistanceCharacterX = "a",
                    EdgeDistanceCharacterY = "AA",
                    Start = WatermarkRange.BottomOfPhoto,
                    End = WatermarkRange.End,
                    RelativePositionMode = RelativePositionMode.LastRow
                },
                new ConnectionMode
                {
                    Ids = new List<string>(){ image.Properties[3].ID, image.Properties[4].ID },
                    RowHeightMinFontPercent = 30,
                    X = PositionBase.Left,
                    Y = PositionBase.Center,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    EdgeDistanceCharacterX = "aAAA",
                    EdgeDistanceCharacterY = "AA",
                    Start = WatermarkRange.BottomOfPhoto,
                    End = WatermarkRange.End,
                    RelativePositionMode = RelativePositionMode.Global
                }
            };

            //image = Global.InitConfig().Templates?.PhotoFrame;
            //image.PhotoPath = "C:\\Users\\kingdee\\Pictures\\Camera Roll\\Windows10.jpg";
            //image.Properties[2].ImagePath = "C:\\Users\\kingdee\\Downloads\\苹果.png";
            return image;
        }



    }
}
