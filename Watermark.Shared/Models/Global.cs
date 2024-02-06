using Newtonsoft.Json;
using SkiaSharp;

namespace Watermark.Win.Models
{
    public static class Global
    {
        public static string TemplatesFolder = AppDomain.CurrentDomain.BaseDirectory + "Templates" + System.IO.Path.DirectorySeparatorChar;
        public static string ThumbnailFolder = AppDomain.CurrentDomain.BaseDirectory + "Thumbnails" + System.IO.Path.DirectorySeparatorChar;
        public static string LogoesFolder = AppDomain.CurrentDomain.BaseDirectory + "Logoes" + System.IO.Path.DirectorySeparatorChar;
        public static WMLoginChildModel CurrentUser = new WMLoginChildModel();

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
                ct.PNode = new WMPNode(i, "0");
                for (var j = 0; j < ct.Controls.Count; j++)
                {
                    var mc = ct.Controls[j];
                    mc.PNode = new WMPNode(j, ct.ID);
                    if (mc is WMLine mLine) nc.Lines.Add(mLine);
                    else if (mc is WMLogo mLogo) nc.Logos.Add(mLogo);
                    else if (mc is WMText mText) nc.Texts.Add(mText);
                    else if (mc is WMContainer mContainer)
                    {
                        for (var k = 0; k < mContainer.Controls.Count; k++)
                        {
                            var child = mContainer.Controls[k];
                            child.PNode = new WMPNode(k, mContainer.ID);
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
                using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 70);
                ImagesBase64[id] = "data:image/jpeg;base64," + Convert.ToBase64String(data.ToArray());
            }
            else
            {
                ImagesBase64[id] = "";
            }
        }

        public static void ImageFile2Base64(Dictionary<string, string> ImagesBase64, byte[] destFile, string id)
        {
            if (destFile == null)
            {
                ImagesBase64[id] = "";
                return;
            }
            using var bitmap = SkiaSharp.SKBitmap.Decode(destFile);
            if (bitmap != null)
            {
                using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 70);
                ImagesBase64[id] = "data:image/jpeg;base64," + Convert.ToBase64String(data.ToArray());
            }
            else
            {
                ImagesBase64[id] = "";
            }
        }

        public static void WriteThumbnailImage(SKBitmap source, string target)
        {
            double w = source.Width, h = source.Height;
            var xs = 1080.0 / h;
            var resized = source.Resize(new SkiaSharp.SKImageInfo((int)(w * xs), (int)(h * xs)), SkiaSharp.SKFilterQuality.Medium);
            using var image = SKImage.FromBitmap(resized);
            using var writeStream = File.OpenWrite(target);
            image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 70).SaveTo(writeStream);
        }

        public static Task WriteThumbnailImageAsync(SKBitmap source, string target)
        {
            return Task.Run(() =>
            {
                WriteThumbnailImage(source, target);
                return Task.CompletedTask;
            });
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

        public static string GetGanZhi(int year)
        {
            string[] gan = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
            string[] zhi = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };

            // 计算干支
            int baseYear = 1924; // 甲子年对应的公元年份
            int offset = year - 3;
            int ganIndex = (offset) % 10 == 0 ? gan.Length - 1 : offset % 10; // 天干循环60年一轮，故加上36
            int zhiIndex = (offset) % 12 == 0 ? zhi.Length - 1 : offset % 12; // 地支同理

            string ganZhi = gan[ganIndex - 1] + zhi[zhiIndex - 1];
            return ganZhi;
        }

        public static string GetMonth(int month)
        {
            string[] mon = { "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖", "拾", "冬", "腊" };
            return mon[month - 1];
        }

        public static string GetDay(int day)
        {
            Dictionary<double, string> days = new Dictionary<double, string>();
            days[0] = "十";
            days[1] = "一";
            days[2] = "二";
            days[3] = "三";
            days[4] = "四";
            days[5] = "五";
            days[6] = "六";
            days[7] = "七";
            days[8] = "八";
            days[9] = "九";
            var rst = "";
            foreach(var d in day.ToString())
            {
                try
                {
                    rst += days[Char.GetNumericValue(d)];
                }
                catch { }
            }

            return rst;
        }
    }
}
