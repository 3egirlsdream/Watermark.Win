using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Media.TextFormatting;
using System.Threading;
using JointWatermark.Views;
using System.Reflection;
using SixLabors.ImageSharp.Processing.Processors;
using JointWatermark.Enums;

namespace JointWatermark.Class
{
    public class ImagesHelper
    {
        public static ImagesHelper Current = new ImagesHelper();
        public DateTime LastDate = DateTime.Now.AddSeconds(-1.1);
        public float FontXS { get; set; }
        /// <summary>
        /// 对角线
        /// </summary>
        Func<int, int, int> Diagonal => new Func<int, int, int>((int a, int b) =>
        {
            return (int)Math.Sqrt((double)a * a + (double)b * b) + 1000;
        });

        /// <summary>
        /// 生成图片
        /// </summary>
        /// <returns></returns>
        public Task<Image> MergeWatermark(ImageProperties properties, bool isPreview = false)
        {
            return CreateWatermark(properties, isPreview).ContinueWith<Image>(t =>
            {
                var path = isPreview ? properties.ThumbnailPath : properties.Path;
                using (var img = Image.Load(path))
                {
                    //旋转图片
                    for (int i = 0; i < properties.Config.RotateCount; i++)
                    {
                        img.Mutate(x => x.Rotate(RotateMode.Rotate90));
                    }
                    //添加文字水印
                    foreach (var word in properties.Config.CharacterWatermarks)
                    {
                        DrawCharacterWord(properties, img, word);
                    }

                    //拼接图片
                    var borderWidth = (int)(properties.Config.BorderWidth * img.Width / 100.0);
                    var resultImage = img.Clone(c => c.Resize(img.Width + 2 * borderWidth, (int)(t.Result.Height + img.Height + borderWidth)));

                    if (img.Metadata.ExifProfile != null)
                    {
                        var by = img.Metadata.ExifProfile.ToByteArray();
                        resultImage.Metadata.ExifProfile = new ExifProfile(by);
                    }

                    resultImage.Metadata.HorizontalResolution = img.Metadata.HorizontalResolution;
                    resultImage.Metadata.VerticalResolution = img.Metadata.VerticalResolution;
                    var polygon = new SixLabors.ImageSharp.Drawing.RegularPolygon(0, 0, resultImage.Width, Diagonal(resultImage.Height, resultImage.Width));
                    resultImage.Mutate(c => c.Fill(SixLabors.ImageSharp.Color.ParseHex("#FFF"), polygon));
                    resultImage.Mutate(c => c.Fill(SixLabors.ImageSharp.Color.ParseHex(properties.Config.BackgroundColor), polygon));
                    var border = new SixLabors.ImageSharp.Point(borderWidth, borderWidth);
                    resultImage.Mutate(x => x.DrawImage(img, border, 1));
                    border.Y += img.Height;
                    resultImage.Mutate(x => x.DrawImage(t.Result, border, 1));
                    ScalePicture(resultImage);

                    t.Result.Dispose();
                    return resultImage;
                }
            });
        }

        /// <summary>
        /// 缩图
        /// </summary>
        /// <param name="resultImage"></param>
        private static void ScalePicture(Image resultImage)
        {
            //缩图
            var ros = Global.Resolution == "1080" ? 1080 : 2160;
            if (Global.Resolution != "default" && resultImage.Width > ros && resultImage.Height > ros)
            {
                int minSide = Math.Min(resultImage.Width, resultImage.Height);
                decimal resolition = (decimal)minSide / ros;
                var w = resultImage.Width < resultImage.Height ? ros : (int)(resultImage.Width / resolition);
                var h = resultImage.Height < resultImage.Width ? ros : (int)(resultImage.Height / resolition);
                resultImage.Mutate(x => x.Resize(w, h));
            }
        }

        private void DrawCharacterWord(ImageProperties properties, Image img, CharacterWatermarkProperty word)
        {
            var family = SetFamily(word.FontFamily).Item1.Value;
            var fontStyle = word.FontStyle;
            Font font = family.CreateFont(word.FontSize * FontXS, SixLabors.Fonts.FontStyle.Regular);
            var start = word.X == 0 ? 0 : img.Width * word.X / 100;
            var startHeight = word.Y == 0 ? 0 : img.Height * word.Y / 100;
            var Params = new PointF(start, (int)startHeight);
            img.Mutate(x => x.DrawText(word.Content, font, SixLabors.ImageSharp.Color.ParseHex(word.Color), Params));
        }

        /// <summary>
        /// 生成预览图片
        /// </summary>
        /// <returns></returns>
        public Task<Image<Rgba32>> MergeWatermarkPreview(ImageProperties properties, bool isPreview = false)
        {
            return CreateWatermark(properties, isPreview).ContinueWith(t =>
            {
                var path = isPreview ? properties.ThumbnailPath : properties.Path;
                using (var img = Image.Load(path))
                {
                    //旋转图片
                    for (int i = 0; i < properties.Config.RotateCount; i++)
                    {
                        img.Mutate(x => x.Rotate(RotateMode.Rotate90));
                    }

                    //添加文字水印
                    foreach (var word in properties.Config.CharacterWatermarks)
                    {
                        DrawCharacterWord(properties, img, word);
                    }

                    //拼接图片
                    var borderWidth = (int)(properties.Config.BorderWidth * img.Width / 100.0);
                    Image<Rgba32> resultImage = new Image<Rgba32>(img.Width + 2 * borderWidth, (int)(t.Result.Height + img.Height + borderWidth));

                    if (img.Metadata.ExifProfile != null)
                    {
                        var by = img.Metadata.ExifProfile.ToByteArray();
                        resultImage.Metadata.ExifProfile = new ExifProfile(by);
                    }

                    resultImage.Metadata.HorizontalResolution = img.Metadata.HorizontalResolution;
                    resultImage.Metadata.VerticalResolution = img.Metadata.VerticalResolution;
                    var polygon = new SixLabors.ImageSharp.Drawing.RegularPolygon(0, 0, resultImage.Width, Diagonal(resultImage.Height, resultImage.Width));
                    resultImage.Mutate(c => c.Fill(SixLabors.ImageSharp.Color.ParseHex("#FFF"), polygon));
                    resultImage.Mutate(c => c.Fill(SixLabors.ImageSharp.Color.ParseHex(properties.Config.BackgroundColor), polygon));
                    var border = new SixLabors.ImageSharp.Point(borderWidth, borderWidth);
                    resultImage.Mutate(x => x.DrawImage(img, border, 1));
                    border.Y += img.Height;
                    resultImage.Mutate(x => x.DrawImage(t.Result, border, 1));
                    t.Result.Dispose();
                    return resultImage;
                }
            });
        }


        /// <summary>
        /// 创建水印
        /// </summary>
        /// <returns></returns>
        public Task<Image<Rgba32>> CreateWatermark(ImageProperties properties, bool isPreview = false)
        {
            return Task.Run<Image<Rgba32>>(() =>
            {
                LastDate = DateTime.Now;
                var path = isPreview ? properties.ThumbnailPath : properties.Path;
                using (var img = Image.Load(path, out IImageFormat format))
                {
                    //旋转图片
                    for (int i = 0; i < properties.Config.RotateCount; i++)
                    {
                        img.Mutate(x => x.Rotate(RotateMode.Rotate90));
                    }

                    var imageMetaData = img.Metadata;
                    var w = img.Width;
                    var h = img.Height * 0.13;
                    if (img.Width < img.Height)
                    {
                        h = img.Height * 0.13 * 0.8;
                    }
                    Image logo;
                    if (string.IsNullOrEmpty(properties.Config.LogoName))
                    {
                        logo = new Image<Rgba32>(40, 40);
                    }
                    else
                    {
                        var p = Global.Path_logo + Global.SeparatorChar + properties.Config.LogoName;
                        if (properties.Config.IsCloudIcon)
                        {
                            using (var mywebclient = new WebClient())
                            {
                                byte[] Bytes = mywebclient.DownloadData(properties.Config.LogoName);
                                logo = Image.Load(Bytes);
                            }
                        }
                        else
                        {
                            logo = Image.Load(p);
                        }
                    }

                    //logo比例系数
                    double xs = (double)(h / 2) / logo.Height;
                    //字体比例系数
                    float fontxs = ((float)h / 156) * properties.Config.FontXS;
                    if (fontxs < 1) fontxs = 1;
                    if (img.Width < img.Height)
                    {
                        fontxs *= 0.8f;
                        xs *= 0.8;
                    }
                    FontXS = fontxs;

                    //下面定义一个矩形区域      
                    var waterWidth = (int)(logo.Width * xs);
                    var waterHeight = (int)(logo.Height * xs);
                    logo.Mutate(x => x.Resize(waterWidth, waterHeight));

                    Image<Rgba32> wm = new Image<Rgba32>(w, (int)h);


                    //IPath yourPolygon = new SixLabors.ImageSharp.Drawing.RegularPolygon(0, 0, w, Diagonal(img.Height, img.Width));
                    //wm.Mutate(c => c.Fill(SixLabors.ImageSharp.Color.ParseHex(properties.Config.BackgroundColor), yourPolygon));
                    SixLabors.Fonts.FontFamily family;
                    var collection = new FontCollection();
                    var f2 = SetFamily(properties.Config.FontFamily);
                    family = f2.Item1.Value;
                    var familyBold = f2.Item1.Value;
                    if (f2.Item2 != null) familyBold = f2.Item2.Value;
                    //var font = new Font(fo, 1350, SixLabors.Fonts.FontStyle.Regular);


                    //右侧F ISO MM字体参数
                    float fontSize = 31 * fontxs;
                    Font font = familyBold.CreateFont(fontSize, SixLabors.Fonts.FontStyle.Bold);
                    var font20 = (24 * fontxs);
                    var TextSize = TextMeasurer.Measure(properties.Config.RightPosition1, new SixLabors.Fonts.TextOptions(font));
                    var oneSize = TextMeasurer.Measure("A", new SixLabors.Fonts.TextOptions(font));
                    var padding_right = TextMeasurer.Measure("23mmmm", new SixLabors.Fonts.TextOptions(font));

                    //计算水印2行文字的总体高度
                    var _font = family.CreateFont(font20, SixLabors.Fonts.FontStyle.Regular);
                    var _fontSize = TextMeasurer.Measure("A", new SixLabors.Fonts.TextOptions(_font));
                    var twoLineWordTotalHeight = 1.04 * TextSize.Height + _fontSize.Height;

                    //绘制第右侧一行文字
                    var start = w - TextSize.Width - padding_right.Width;
                    var startHeight = (h - twoLineWordTotalHeight) / 2;
                    var Params = new PointF(start, (int)startHeight);
                    wm.Mutate(x => x.DrawText(properties.Config.RightPosition1, font, SixLabors.ImageSharp.Color.ParseHex(properties.Config.Row1FontColor), Params));

                    //绘制右侧第二行文字
                    font = family.CreateFont(font20, SixLabors.Fonts.FontStyle.Regular);

                    var XY = new PointF(Params.X, (int)(Params.Y + 1.04 * TextSize.Height));
                    wm.Mutate(x => x.DrawText(properties.Config.RightPosition2, font, SixLabors.ImageSharp.Color.FromRgb(145, 145, 145), XY));

                    //绘制竖线
                    var font20Size = TextMeasurer.Measure("A", new SixLabors.Fonts.TextOptions(font));
                    var lStart = new PointF(Params.X - (int)(oneSize.Width * 0.6), (int)(0.5*(wm.Height - logo.Height)));
                    var lEnd = new PointF(lStart.X, wm.Height - lStart.Y);
                    wm.Mutate(x => x.DrawLines(SixLabors.ImageSharp.Color.LightGray, 2 * fontxs, lStart, lEnd));

                    //绘制LOGO
                    var line = new SixLabors.ImageSharp.Point((int)(lStart.X - (int)(oneSize.Width * 0.6) - logo.Width), (int)(0.5*(wm.Height - logo.Height)));
                    wm.Mutate(x => x.DrawImage(logo, line, 1));
                    logo.Dispose();

                    //左边距系数
                    var leftWidth = (double)1 / 25 * wm.Width;// 100 * fontxs * 100 / 156;

                    //绘制设备信息
                    var font28 = (34 * fontxs);
                    font = familyBold.CreateFont(font28, SixLabors.Fonts.FontStyle.Bold);
                    var Producer = new PointF((int)(leftWidth), Params.Y);
                    wm.Mutate(x => x.DrawText(properties.Config.LeftPosition1, font, SixLabors.ImageSharp.Color.ParseHex(properties.Config.Row1FontColor), Producer));

                    //绘制时间
                    font = family.CreateFont(font20, SixLabors.Fonts.FontStyle.Regular);
                    var Date = new PointF(Producer.X, XY.Y);
                    wm.Mutate(x => x.DrawText(properties.Config.LeftPosition2, font, SixLabors.ImageSharp.Color.FromRgb(145, 145, 145), Date));

                    return wm;

                }
            });
        }

        private static Tuple<SixLabors.Fonts.FontFamily?, SixLabors.Fonts.FontFamily?> SetFamily(string FontFamily)
        {
            SixLabors.Fonts.FontFamily? family = null;
            SixLabors.Fonts.FontFamily? familyBold = null;

            if (Global.FontResourrce.TryGetValue(FontFamily, out byte[] bt))
            {
                using (var ms = new MemoryStream(bt))
                {
                    var collection = new FontCollection();
                    family = collection.Add(ms);
                    if(FontFamily == "OpenSans")
                    {
                        using (var ms2 = new MemoryStream(Properties.Resources.OpenSans_Bold))
                        {
                            familyBold = collection.Add(ms2);
                        }
                    }
                }
            }
            else
            {
                var collection = new FontCollection();
                if (File.Exists("./fonts/" + FontFamily + ".ttf"))
                {
                    family = collection.Add($"./fonts/{FontFamily}.ttf");
                }
                if (File.Exists("./fonts/" + FontFamily + "-Bold.ttf"))
                {
                    familyBold = collection.Add($"./fonts/{FontFamily}-Bold.ttf");
                }
            }

            return Tuple.Create(family, familyBold);
        }


        /// <summary>
        /// 创建水印
        /// </summary>
        /// <returns></returns>
        public Task<Image<Rgba32>> CreateWatermark(ImageConfig config)
        {
            return Task.Run(() =>
            {
                LastDate = DateTime.Now;
                using (var img = new Image<Rgba32>(1920, 1080))
                {
                    var imageMetaData = img.Metadata;
                    var w = img.Width;
                    var h = img.Height * 0.13;
                    if (img.Width < img.Height)
                    {
                        h = img.Height * 0.13 * 0.4;
                    }
                    var p = Global.Path_logo + Global.SeparatorChar + config.LogoName;
                    Image logo;
                    if (string.IsNullOrEmpty(config.LogoName))
                    {
                        logo = Image.Load(Properties.Resources.leica);
                    }
                    else
                    {
                        logo = Image.Load(p);
                    }

                    //logo比例系数
                    double xs = (double)(h / 2) / logo.Height;
                    //字体比例系数
                    float fontxs = ((float)h / 156);
                    if (fontxs < 1) fontxs = 1;
                    if (img.Width < img.Height)
                    {
                        fontxs *= 0.8f;
                        xs *= 0.8;
                    }


                    //下面定义一个矩形区域      
                    var waterWidth = (int)(logo.Width * xs);
                    var waterHeight = (int)(logo.Height * xs);
                    logo.Mutate(x => x.Resize(waterWidth, waterHeight));

                    Image<Rgba32> wm = new Image<Rgba32>(w, (int)h);
                    IPath yourPolygon = new SixLabors.ImageSharp.Drawing.RegularPolygon(0, 0, w, Diagonal(img.Height, img.Width));
                    var f = SetFamily(config.FontFamily);
                    SixLabors.Fonts.FontFamily family = f.Item1.Value;

                    //右侧F ISO MM字体参数
                    float fontSize = 31 * fontxs;
                    Font font = family.CreateFont(fontSize, SixLabors.Fonts.FontStyle.Bold);
                    var TextSize = TextMeasurer.Measure(config.RightPosition1, new SixLabors.Fonts.TextOptions(font));
                    var oneSize = TextMeasurer.Measure("A", new SixLabors.Fonts.TextOptions(font));
                    var padding_right = TextMeasurer.Measure("23mmmm", new SixLabors.Fonts.TextOptions(font));

                    //绘制第右侧一行文字
                    var start = w - TextSize.Width - padding_right.Width;
                    var Params = new PointF(start, (int)(0.3 * h));
                    wm.Mutate(x => x.DrawText(config.RightPosition1, font, SixLabors.ImageSharp.Color.ParseHex(config.Row1FontColor), Params));

                    //绘制右侧第二行文字
                    var font20 = (24 * fontxs);
                    font = family.CreateFont(font20, SixLabors.Fonts.FontStyle.Regular);

                    var XY = new PointF(Params.X, (int)(Params.Y + 1.04 * TextSize.Height));
                    wm.Mutate(x => x.DrawText(config.RightPosition2, font, SixLabors.ImageSharp.Color.FromRgb(145, 145, 145), XY));

                    //绘制竖线
                    var font20Size = TextMeasurer.Measure("A", new SixLabors.Fonts.TextOptions(font));
                    var lStart = new PointF(Params.X - (int)(oneSize.Width * 0.6), (int)(0.5*(wm.Height - logo.Height)));
                    var lEnd = new PointF(lStart.X, wm.Height - lStart.Y);
                    wm.Mutate(x => x.DrawLines(SixLabors.ImageSharp.Color.LightGray, 2 * fontxs, lStart, lEnd));

                    //绘制LOGO
                    var line = new SixLabors.ImageSharp.Point((int)(lStart.X - (int)(oneSize.Width * 0.6) - logo.Width), (int)(0.5*(wm.Height - logo.Height)));
                    wm.Mutate(x => x.DrawImage(logo, line, 1));
                    logo.Dispose();

                    //左边距系数
                    var leftWidth = (double)1 / 25 * wm.Width;// 100 * fontxs * 100 / 156;

                    //绘制设备信息
                    var font28 = (34 * fontxs);
                    font = family.CreateFont(font28, SixLabors.Fonts.FontStyle.Bold);
                    var Producer = new PointF((int)(leftWidth), Params.Y);
                    wm.Mutate(x => x.DrawText(config.LeftPosition1, font, SixLabors.ImageSharp.Color.ParseHex(config.Row1FontColor), Producer));

                    //绘制时间
                    font = family.CreateFont(font20, SixLabors.Fonts.FontStyle.Regular);
                    var Date = new PointF(Producer.X, XY.Y);
                    wm.Mutate(x => x.DrawText(config.LeftPosition2, font, SixLabors.ImageSharp.Color.FromRgb(145, 145, 145), Date));

                    return wm;

                }
            });
        }

        /// <summary>
        /// 转为WPF Image组件可以识别的类型
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        public ImageSource ImageSharpToImageSource(SixLabors.ImageSharp.Image<Rgba32> image)
        {
            var bmp = new WriteableBitmap(image.Width, image.Height, image.Metadata.HorizontalResolution, image.Metadata.VerticalResolution, PixelFormats.Bgra32, null);

            bmp.Lock();
            try
            {
                var backBuffer = bmp.BackBuffer;

                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        var backBufferPos = backBuffer + (y * image.Width + x) * 4;
                        var rgba = image[x, y];
                        var color = rgba.A << 24 | rgba.R << 16 | rgba.G << 8 | rgba.B;

                        System.Runtime.InteropServices.Marshal.WriteInt32(backBufferPos, color);
                    }
                }

                bmp.AddDirtyRect(new Int32Rect(0, 0, image.Width, image.Height));
            }
            finally
            {
                bmp.Unlock();
            }
            return bmp;
        }


        /// <summary>
        /// 读取Exif信息
        /// </summary>
        /// <param name="filenames"></param>
        /// <param name="logoName"></param>
        /// <returns></returns>
        public ImageProperties ReadImage(string filenames, string logoName)
        {
            var i = new ImageProperties(filenames, filenames.Substring(filenames.LastIndexOf(Global.SeparatorChar) + 1));
            var meta = Global.GetThumbnailPath(i.Path);
            i.ThumbnailPath = meta.path;
            i.Config.LeftPosition1 = meta.left1;
            i.Config.LeftPosition2 = meta.left2;
            i.Config.RightPosition1 = meta.right1;
            i.Config.RightPosition2 = meta.right2;
            i.Config.BackgroundColor = "#fff";
            i.Path = filenames;
            i.Config.LogoName = logoName;
            return i;
        }


        public Task<Image> SplitImages(ObservableCollection<ImageProperties> images, bool horizon, CancellationToken token, Loading loading)
        {
            return Task.Run(() =>
            {

                loading.ISetPosition(1, $"已完成：1%");
                List<SixLabors.ImageSharp.Image> list = new List<SixLabors.ImageSharp.Image>();

                foreach (var image in images)
                {
                    list.Add(SixLabors.ImageSharp.Image.Load(image.Path));
                }

                var maxWidth = list.Max(c => c.Width);
                var maxHeight = list.Max(c => c.Height);
                foreach (var ls in list)
                {
                    if (horizon)
                    {
                        if (ls.Height !=  maxHeight)
                        {
                            var xs = (double)maxHeight / (double)ls.Height;
                            ls.Mutate(c => c.Resize((int)(ls.Width * xs), maxHeight));
                        }
                    }
                    else
                    {
                        if (ls.Width !=  maxWidth)
                        {
                            var xs = (double)maxWidth / (double)ls.Width;
                            ls.Mutate(c => c.Resize(maxWidth, (int)(ls.Height * xs)));
                        }
                    }

                }

                loading.ISetPosition(5, $"已完成：5%");
                double borderWidth = 0;
                double w = 0; double h = 0;
                var firstImage = images.FirstOrDefault();
                if (horizon)
                {
                    borderWidth = firstImage.Config.BorderWidth * maxHeight / 100.0;
                    w = 2 * borderWidth + list.Sum(c => c.Width);
                    h = 2 * borderWidth + maxHeight;
                }
                else
                {
                    borderWidth = firstImage.Config.BorderWidth * maxWidth / 100.0;
                    w = 2 * borderWidth + maxWidth;
                    h = 2 * borderWidth + list.Sum(c => c.Height);
                }
                var result = list[0].Clone(c => c.Resize((int)w, (int)h));
                var polygon = new SixLabors.ImageSharp.Drawing.RegularPolygon(0, 0, result.Width, Diagonal(result.Height, result.Width));
                result.Mutate(c => c.Fill(SixLabors.ImageSharp.Color.ParseHex("#FFF"), polygon));
                var start = new SixLabors.ImageSharp.Point((int)borderWidth, (int)borderWidth);
                loading.ISetPosition(10, $"已完成：10%");
                foreach (var item in list)
                {
                    token.ThrowIfCancellationRequested();
                    result.Mutate(x => x.DrawImage(item, start, 1));
                    if (horizon)
                        start.X += item.Width;
                    else
                        start.Y += item.Height;
                    var c = (int)(10 + list.IndexOf(item) + 1 * 80 / (double)list.Count);
                    loading.ISetPosition(c, $"已完成：{c}%");
                }
                list.ForEach(c => c.Dispose());
                return result;
            });
        }


        #region 2.0

        public Task<Image<Rgba32>> Generation(GeneralWatermarkProperty image, bool isPreview = false)
        {
            LastDate = DateTime.Now;
            return Task.Run(() =>
            {
                var Properties = image.Properties.Where(c => c.IsChecked).ToList();
                var ConnectionModes = image.ConnectionModes.Where(c => c.IsChecked).ToList();
                var _uids = ConnectionModes.Select(c => new { c.Ids, c.ID }).ToList();
                for (var i = 0; i < _uids.Count; i++)
                {
                    var _u = _uids[i].Ids;
                    if (image.Properties.Where(c => _u.Contains(c.ID)).All(c => !c.IsChecked))
                    {
                        var idx = ConnectionModes.FirstOrDefault(c => c.ID == _uids[i].ID);
                        if (idx != null)
                            ConnectionModes.Remove(idx);
                    }
                }
                int resultWidth, resultHeight, shortLine;
                double shortLineBorderPercent;
                string PhotoPath = isPreview ? image.ThumbnailPath : image.PhotoPath;
                using (var orientImg = Image.Load<Rgba32>(PhotoPath))
                {
                    var img = orientImg.Clone(x => x.AutoOrient());
                    shortLine = Math.Min(img.Height, img.Width);
                    double pw = image.PecentOfWidth;
                    double ph = image.PecentOfHeight;

                    shortLineBorderPercent = (100 - Math.Min(pw, ph)) / 100.0;
                    //根据边框的起始位置计算边框宽度
                    var XBorderWidth = image.StartPosition.X * shortLine / 100.0;
                    var YBorderWidth = image.StartPosition.Y * shortLine / 100.0;
                    if (image.EnableFixedPercent)
                    {
                        if (pw > ph)
                        {
                            resultHeight = (int)(img.Height + shortLine * shortLineBorderPercent);
                            resultWidth = (int)(img.Width + XBorderWidth * 2);
                        }
                        else
                        {
                            resultHeight = (int)(img.Height + YBorderWidth * 2);
                            resultWidth = (int)(img.Width + shortLine * shortLineBorderPercent);
                        }
                    }
                    else
                    {
                        resultHeight = (int)(img.Height / (double)ph) * 100;
                        resultWidth = (int)(img.Width / (double)pw) * 100;
                    }
                    //基础字体系数
                    var fontxs = (img.Height * 0.13 / 156);
                    if (img.Height > img.Width) fontxs *= 0.8;
                    if (fontxs == 0) fontxs = 1;

                    var resultImage = img.Clone(x => x.Resize(resultWidth, resultHeight));
                    var polygon = new RegularPolygon(0, 0, resultWidth, Diagonal(resultHeight, resultWidth));
                    //填充背景色
                    resultImage.Mutate(x => x.Fill(SixLabors.ImageSharp.Color.ParseHex(image.BackgroundColor), polygon));
                    //图片起始位置
                    var start = new SixLabors.ImageSharp.Point((int)XBorderWidth, (int)YBorderWidth);
                    if (!image.EnableFixedPercent)
                    {
                        start = new SixLabors.ImageSharp.Point((int)XBorderWidth, image.StartPosition.Y * img.Height / 100);
                    }
                    if (image.Shadow.Enabled)
                    {
                        var shadowWidth = (int)(image.Shadow.Width * fontxs);
                        var rec = new SixLabors.ImageSharp.Rectangle(start.X - shadowWidth, start.Y - shadowWidth, img.Width + 2 * shadowWidth, img.Height + 2 * shadowWidth);
                        var blackImg = resultImage.Clone(cc => cc.Resize(img.Width, img.Height));
                        blackImg.Mutate(x => x.Fill(SixLabors.ImageSharp.Color.ParseHex("#696969"), polygon));
                        resultImage.Mutate(x => x.DrawImage(blackImg, start, 1).BoxBlur((int)(50 * fontxs), rec));
                    }
                    //绘制图片
                    resultImage.Mutate(x => x.DrawImage(img, start, 1));

                    //绘制边框图片
                    if(image.ImageBackgroud != null && image.ImageBackgroud.Type == ImageBackgroudType.Image)
                    {
                        Image bakTop, bakBot;
                        if (image.ImageBackgroud.Top.StartsWith("system"))
                        {
                            bakTop = Image.Load(Global.SystemImage["system_t"]);
                        }
                        else bakTop = Image.Load(image.ImageBackgroud.Top);
                        if (image.ImageBackgroud.Top.StartsWith("system"))
                        {
                            bakBot = Image.Load(Global.SystemImage["system_b"]);
                        }
                        else bakBot = Image.Load(image.ImageBackgroud.Bottom);

                        var borderBakTop = resultHeight * (image.StartPosition.Y / 100.0);
                        var borderBakBot = resultHeight * (100 - image.PecentOfHeight - image.StartPosition.Y) / 100.0;// img.Height - borderBakTop;
                        var topXs = borderBakTop  * 1.0 /  bakTop.Height;
                        var botXs = borderBakBot * 1.0 / bakBot.Height;

                        var bakTopWidth = (int)(topXs * bakTop.Width);
                        var bakBotWidth = (int)(botXs * bakBot.Width);
                        bakTop.Mutate(c => c.Resize(bakTopWidth, (int)borderBakTop));
                        bakBot.Mutate(c => c.Resize(bakBotWidth, (int)borderBakBot));


                        var bakImgStart = new SixLabors.ImageSharp.Point(0, 0);
                        for (int i = 0; i < resultWidth;)
                        {
                            resultImage.Mutate(x => x.DrawImage(bakTop, bakImgStart, 1));
                            i += bakTop.Width;
                            bakImgStart.X = i;
                        }
                        bakImgStart.Y = resultHeight - bakBot.Height;
                        bakImgStart.X = 0;
                        for (int i = 0; i < resultWidth;)
                        {
                            resultImage.Mutate(x => x.DrawImage(bakBot, bakImgStart, 1));
                            i += bakBot.Width;
                            bakImgStart.X = i;
                        }

                    }

                    //绘制文字
                    var connectedIds = new List<string>();
                    ConnectionModes.ForEach(c =>
                    {
                        connectedIds.AddRange(c.Ids);
                    });
                    //绘制单体
                    foreach (GeneralWatermarkRowProperty row in Properties.Where(c => connectedIds.All(x => c.ID != x)))
                    {
                        double xW = 0, yW = 0;
                        if (row.EdgeDistanceType == EdgeDistanceType.Character)
                        {
                            Font font = GetFont(row.FontFamily, row.IsBold, row.FontSize * fontxs * row.FontXS);
                            //测量宽度像素
                            var XTextSize = TextMeasurer.Measure(row.EdgeDistanceCharacterX, new SixLabors.Fonts.TextOptions(font));
                            var content = Global.GetContent(row, image.Meta);
                            var ContentTextSize = TextMeasurer.Measure(content, new SixLabors.Fonts.TextOptions(font));
                            var YTextSize = XTextSize.Height * row.EdgeDistanceCharacterY.Length;

                            ConnectionMode lastRow = null;
                            double contentHeight = ContentTextSize.Height;
                            GetWatermarkStartPosition(resultWidth, resultHeight, img, start, row, ref xW, ref yW, YTextSize, XTextSize.Width, ref contentHeight, ContentTextSize.Width, lastRow);

                            var Params = new SixLabors.ImageSharp.Point((int)xW, (int)yW);
                            var color = SixLabors.ImageSharp.Color.ParseHex(row.Color);
                            
                            resultImage.Mutate(x => x.DrawText(content, font, color.WithAlpha(0.3f), Params));
                        }
                    }

                    //绘制组合
                    for (var i = 0; i < ConnectionModes.Count; i++)
                    {
                        var row = ConnectionModes[i];
                        //DrawWatermark(image, resultWidth, resultHeight, img, resultImage, start, fontxs, row, i);
                    }

                    ScalePicture(resultImage);

                    return resultImage;
                }
            });
        }

        private void DrawWatermark(GeneralWatermarkProperty image, int resultWidth, int resultHeight, Image<Rgba32> img, Image<Rgba32> resultImage, SixLabors.ImageSharp.Point start, double fontxs, ConnectionMode row, int index)
        {
            //取出水印组合
            var group = image.Properties.Where(c => row.Ids.Any(x => x == c.ID) && c.IsChecked).ToList();
            double totalHeight = 0, rowHeight = 0;
            /**计算组合的整体高度
             *
             * 首先判断行高
             */
            if (row.RowHeightMinFontPercent != null)
            {
                //找出最小字体
                var minFontSizeRow = group.OrderBy(c => c.FontSize).FirstOrDefault(c=>c.ContentType == ContentType.Text);
                if (minFontSizeRow != null)
                {
                    var fONT = GetFont(minFontSizeRow.FontFamily, minFontSizeRow.IsBold, minFontSizeRow.FontSize * fontxs * minFontSizeRow.FontXS);
                    var cnt = Global.GetContent(minFontSizeRow, image.Meta);
                    rowHeight = TextMeasurer.Measure(cnt, new SixLabors.Fonts.TextOptions(fONT)).Height * row.RowHeightMinFontPercent.Value / 100;
                }
                //算出整体高度
                foreach (var r in group)
                {
                    if (r.ContentType == ContentType.Text)
                    {
                        var fONT = GetFont(r.FontFamily, r.IsBold, r.FontSize * fontxs * r.FontXS);
                        var cnt = Global.GetContent(r, image.Meta);
                        var rHeight = TextMeasurer.Measure(cnt, new SixLabors.Fonts.TextOptions(fONT)).Height;
                        totalHeight += rHeight;
                    }
                    else if (r.ContentType == ContentType.Image)
                    {
                        var rHeight = (resultHeight - start.Y - img.Height) * r.ImagePercentOfRange / 100;
                        totalHeight += rHeight;
                    }
                }
                //加上行高
                if (group.Count > 1)
                {
                    totalHeight += rowHeight * (group.Count - 1);
                }
            }
            double xW = 0, yW = 0;

            //计算整体宽度，按最长的文字行计算
            var longestRow = group.OrderByDescending(c =>
            {
                var content = Global.GetContent(c, image.Meta);
                var _f = GetFont(c.FontFamily, c.IsBold, c.FontSize * fontxs * c.FontXS);
                var wth = TextMeasurer.Measure(content, new SixLabors.Fonts.TextOptions(_f));
                return wth.Width;
            }).FirstOrDefault();
            var longestRowFont = GetFont(longestRow.FontFamily, longestRow.IsBold, longestRow.FontSize * fontxs * longestRow.FontXS);
            var longestContent = Global.GetContent(longestRow, image.Meta);
            var totalWidth = TextMeasurer.Measure(longestContent, new SixLabors.Fonts.TextOptions(longestRowFont)).Width;

            //计算边距
            double borderHeight, borderWidth;
            if (row.EdgeDistanceType == EdgeDistanceType.Character)
            {
                var borderFont = GetFont(longestRow.FontFamily, false, longestRow.FontSize * fontxs * longestRow.FontXS);
                var border = TextMeasurer.Measure(row.EdgeDistanceCharacterX, new SixLabors.Fonts.TextOptions(borderFont));
                borderHeight = border.Height * row.EdgeDistanceCharacterY.Length;
                borderWidth = border.Width;
            }
            else
            {
                borderHeight = row.EdgeDistanceFixedPixel * fontxs;
                borderWidth = row.EdgeDistanceFixedPixel * fontxs;
            }
            //计算整体宽高
            if (group.All(c => c.ContentType == ContentType.Image))
            {
                var g = group[0];
                if (!string.IsNullOrEmpty(g.ImagePath.Path))
                {
                    Image logo = LoadLogo(g);

                    //resultHeight - start.Y - img.Height 为白边的高度
                    var xs = (resultHeight - start.Y - img.Height) * 1.0 / logo.Height * g.ImagePercentOfRange / 100.0;
                    totalWidth = (int)(logo.Width * xs);
                    totalHeight = (int)(logo.Height * xs);
                }
            }
            else if (group.Any(c => c.ContentType == ContentType.Image)&& group.Any(c => c.ContentType == ContentType.Text))
            {
                var g = group.FirstOrDefault(c => c.ContentType == ContentType.Image);
                if (!string.IsNullOrEmpty(g.ImagePath.Path))
                {
                    var logo = LoadLogo(g);
                    //设计的图片高度
                    var designHeight = (resultHeight - start.Y - img.Height) * 1.0 * g.ImagePercentOfRange / 100.0;
                    //resultHeight - start.Y - img.Height 为白边的高度
                    var xs = designHeight / logo.Height;
                    var _w = (int)(logo.Width * xs);
                    var _h = (int)(logo.Height * xs);
                    if (_w > totalWidth) totalWidth = _w;
                    if (_h > totalHeight) totalHeight = _h;
                }
            }
            else if (group.All(c => c.ContentType == ContentType.Line))
            {
                var g = group[0];
                var font = GetFont(g.FontFamily, g.IsBold, g.FontSize * fontxs * g.FontXS);
                var fontWidth = TextMeasurer.Measure(g.Content, new SixLabors.Fonts.TextOptions(font)).Width;
                totalWidth = (float)(g.LinePixel * fontxs + fontWidth);
                totalHeight = (resultHeight - start.Y - img.Height) / 100.0 * g.LinePercentOfRange;

            }
            ConnectionMode lastRow = index >= 1 ? image.ConnectionModes[index - 1] : null;
            GetWatermarkStartPosition(resultWidth, resultHeight, img, start, row, ref xW, ref yW, borderHeight, borderWidth, ref totalHeight, totalWidth, lastRow);
            row.TotalHeight = totalHeight;
            row.TotalWidth = totalWidth;


            /*
             * 根据row.RelativePositionMode判断是组件定位模式
             * 
             */

            if (row.RelativePositionMode == RelativePositionMode.LastRow)
            {

            }
            else
            {

            }


            row.StartPoint = new SixLabors.ImageSharp.Point((int)xW, (int)yW);




            //开始绘制
            var row1 = row.StartPoint;
            //这里还要考虑组件中每行的相对位置，现在默认只有竖直
            foreach (var item in group)
            {
                if (item.ContentType == ContentType.Text)
                {
                    var _row = new SixLabors.ImageSharp.Point(row1.X, row1.Y);
                    var font = GetFont(item.FontFamily, item.IsBold, item.FontSize * fontxs * item.FontXS);
                    var content = Global.GetContent(item, image.Meta);
                    var _font = TextMeasurer.Measure(content, new SixLabors.Fonts.TextOptions(font));
                    var fontHeight = _font.Height;
                    if(item.X == PositionBase.Center)
                    {
                        _row.X += (int)((totalWidth - _font.Width) / 2.0);
                    }
                    else if (item.X == PositionBase.Right)
                    {
                        _row.X += (int)(totalWidth - _font.Width);
                    }
                    resultImage.Mutate(x => x.DrawText(content, font, SixLabors.ImageSharp.Color.ParseHex(item.Color), _row));
                    row1.Y += (int)(rowHeight + fontHeight);
                }
                else if (item.ContentType == ContentType.Image)
                {
                    if (!string.IsNullOrEmpty(item.ImagePath.Path))
                    {
                        var logo = LoadLogo(item);
                        //resultHeight - start.Y - img.Height 为白边的高度
                        var xs = (resultHeight - start.Y - img.Height) * 1.0 / logo.Height * item.ImagePercentOfRange / 100.0;
                        if (xs == 0) xs = 1;
                        int _w = (int)(logo.Width * xs), _h = (int)(logo.Height * xs);
                        logo.Mutate(x => x.Resize(_w, _h));

                        var _row = new SixLabors.ImageSharp.Point(row1.X, row1.Y);
                        if (item.X == PositionBase.Center)
                        {
                            _row.X += (int)((totalWidth - logo.Width) / 2.0);
                        }
                        else if (item.X == PositionBase.Right)
                        {
                            _row.X += (int)(totalWidth - logo.Width);
                        }

                        var rec = new SixLabors.ImageSharp.Rectangle(_row.X, _row.Y, logo.Width, logo.Height);
                        resultImage.Mutate(x => x.DrawImage(logo, _row, 1));
                        row1.Y += (int)(rowHeight + logo.Height);
                    }
                }
                else if (item.ContentType == ContentType.Line)
                {
                    var _s = new SixLabors.ImageSharp.Point(row1.X, row1.Y);
                    var _e = new SixLabors.ImageSharp.Point(row1.X, row1.Y + (int)totalHeight);
                    var lineWidth = (int)(item.LinePixel * fontxs);
                    resultImage.Mutate(x => x.DrawLines(SixLabors.ImageSharp.Color.ParseHex(item.Color), lineWidth == 0 ? 1 : lineWidth , _s, _e));
                    row1.Y += (int)(totalHeight);
                }
            }
        }

        private static Image LoadLogo(GeneralWatermarkRowProperty g)
        {
            Image logo;
            var logoPath = Global.Path_logo + Global.SeparatorChar + g.ImagePath.Path;
            if (g.ImagePath.IsCloud)
            {
                using (var mywebclient = new WebClient())
                {
                    byte[] Bytes = mywebclient.DownloadData(g.ImagePath.Path);
                    logo = Image.Load(Bytes);
                }
            }
            else
            {
                logo = Image.Load(logoPath);
            }

            return logo;
        }

        private static void GetWatermarkStartPosition(int resultWidth
            , int resultHeight
            , Image<Rgba32> img
            , SixLabors.ImageSharp.Point start
            , WatermarkProperty row
            , ref double xW
            , ref double yW
            , double borderHeight
            , double borderWidth
            , ref double contentHeight
            , double contentWidth
            , ConnectionMode lastRow)
        {
            double x = 0, y = 0;
            //计算起始位置
            if (row.Start != WatermarkRange.None)
            {
                if (row.Start == WatermarkRange.BottomOfPhoto)
                {
                    y = start.Y + img.Height;
                    x = start.X;
                }
                else if (row.Start == WatermarkRange.RightOfPhoto)
                {
                    x = start.X + img.Width;
                }
                else if (row.Start == WatermarkRange.LeftOfPhoto)
                {
                    x = start.X;
                }
                else if (row.Start == WatermarkRange.TopOfPhoto)
                {
                    y = start.Y;
                    x = start.X;
                }
            }
            if (row.RelativePositionMode == RelativePositionMode.LastRow)
            {
                //以上一个组件计算位置的应该不用x y
                if (lastRow == null) return;
                if (row.X == PositionBase.Left) xW = lastRow.TotalWidth + borderWidth;
                else if (row.X == PositionBase.Right) xW = resultWidth - borderWidth - contentWidth - (resultWidth - lastRow.StartPoint.X); //减去固定边距和文字本身的宽度
                else if (row.X  == PositionBase.Center) xW = (resultWidth - contentWidth) / 2;

                if (row.Y == PositionBase.Top) yW = y + borderHeight + lastRow.TotalHeight;
                else if (row.Y == PositionBase.Bottom)
                {
                    yW = resultHeight - borderHeight - lastRow.TotalHeight - contentHeight;
                }
                else if (row.Y == PositionBase.Center) yW = (resultHeight - y - contentHeight) / 2 + y;
            }
            else
            {
                if (row.X == PositionBase.Left) xW = x + borderWidth;
                else if (row.X == PositionBase.Right) xW = resultWidth - borderWidth - contentWidth - x; //减去固定边距和文字本身的宽度
                else if (row.X  == PositionBase.Center) xW = (resultWidth - contentWidth) / 2;

                if (row.Y == PositionBase.Top) yW = y + borderHeight;
                else if (row.Y == PositionBase.Bottom)
                {
                    yW = resultHeight - borderHeight - contentHeight;
                    contentHeight += borderHeight;
                }
                else if (row.Y == PositionBase.Center) yW = (resultHeight - y - contentHeight) / 2 + y;
            }
        }

        private Font GetFont(string fontFamily, bool isBold, double fontSize)
        {
            try
            {
                var f = SetFamily(fontFamily);
                if(f.Item1 == null)
                {
                    throw new Exception("字体不存在，请修改配置的字体");
                }
                var fr = f.Item1.Value;
                var font = fr.CreateFont((int)fontSize, SixLabors.Fonts.FontStyle.Regular);
                if (isBold)
                {
                    var fb = f.Item2 ?? f.Item1.Value;
                    font = fb.CreateFont((int)fontSize, SixLabors.Fonts.FontStyle.Bold);
                }

                return font;
            }
            catch(Exception ex)
            {
                throw ex;
            }
        }


        #endregion
    }
}
