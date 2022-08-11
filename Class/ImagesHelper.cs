﻿using System;
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

namespace JointWatermark.Class
{
    public class ImagesHelper
    {
        public static ImagesHelper Current = new ImagesHelper();
        public DateTime LastDate = DateTime.Now.AddSeconds(-1.1);
        public Task<Image<Rgba32>> MergeWatermark(ImageProperties properties, bool isPreview = false)
        {
            return CreateWatermark(properties, isPreview).ContinueWith(t =>
            {
                var path = isPreview ? properties.ThumbnailPath : properties.Path;
                using (var img = Image.Load(path))
                {
                    //拼接图片
                    var borderWidth = (int)(properties.Config.BorderWidth * img.Width / 100.0);
                    Image<Rgba32> resultImage = new Image<Rgba32>(img.Width + 2 * borderWidth, (int)(t.Result.Height + img.Height + borderWidth));

                    if (img.Metadata.ExifProfile!= null)
                    {
                        var by = img.Metadata.ExifProfile.ToByteArray();
                        resultImage.Metadata.ExifProfile = new ExifProfile(by);
                    }

                    resultImage.Metadata.HorizontalResolution = img.Metadata.HorizontalResolution;
                    resultImage.Metadata.VerticalResolution = img.Metadata.VerticalResolution;
                    var polygon = new SixLabors.ImageSharp.Drawing.RegularPolygon(0, 0, resultImage.Width, 10000);
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


        public Task<Image<Rgba32>> CreateWatermark(ImageProperties properties, bool isPreview = false)
        {
            return Task.Run(() =>
            {
                LastDate = DateTime.Now;
                var path = isPreview ? properties.ThumbnailPath : properties.Path;
                using (var img = Image.Load(path, out IImageFormat format))
                {
                    var imageMetaData = img.Metadata;
                    var w = img.Width;
                    var h = img.Height * 0.13;
                    if (img.Width < img.Height)
                    {
                        h = img.Height * 0.13 * 0.4;
                    }
                    var p = Global.Path_logo + Global.SeparatorChar + properties.Config.LogoName;
                    Image logo;
                    if (string.IsNullOrEmpty(properties.Config.LogoName))
                    {
                        logo = new Image<Rgba32>(40, 40);
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


                    IPath yourPolygon = new SixLabors.ImageSharp.Drawing.RegularPolygon(0, 0, w, 10000);
                    //wm.Mutate(c => c.Fill(SixLabors.ImageSharp.Color.ParseHex(properties.Config.BackgroundColor), yourPolygon));
                    SixLabors.Fonts.FontFamily family;
                    if (properties.Config.FontFamily == "微软雅黑")
                    {
                        family = SixLabors.Fonts.SystemFonts.Get("Microsoft YaHei");
                    }
                    else
                    {
                        byte[] bt = Global.FontResourrce[properties.Config.FontFamily];
                        using (var ms = new MemoryStream(bt))
                        {
                            var collection = new FontCollection();
                            family = collection.Add(ms);
                        }
                    }
                    //var font = new Font(fo, 1350, SixLabors.Fonts.FontStyle.Regular);

                    //右侧F ISO MM字体参数
                    float fontSize = 31 * fontxs;
                    Font font = family.CreateFont(fontSize, SixLabors.Fonts.FontStyle.Bold);
                    var TextSize = TextMeasurer.Measure(properties.Config.RightPosition1, new SixLabors.Fonts.TextOptions(font));
                    var oneSize = TextMeasurer.Measure("A", new SixLabors.Fonts.TextOptions(font));
                    var padding_right = TextMeasurer.Measure("23mmmm", new SixLabors.Fonts.TextOptions(font));

                    //绘制第右侧一行文字
                    var start = w - TextSize.Width - padding_right.Width;
                    var Params = new PointF(start, (int)(0.3 * h));
                    wm.Mutate(x => x.DrawText(properties.Config.RightPosition1, font, SixLabors.ImageSharp.Color.ParseHex(properties.Config.Row1FontColor), Params));

                    //绘制右侧第二行文字
                    var font20 = (24 * fontxs);
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
                    font = family.CreateFont(font28, SixLabors.Fonts.FontStyle.Bold);
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


                    IPath yourPolygon = new SixLabors.ImageSharp.Drawing.RegularPolygon(0, 0, w, 10000);
                    //wm.Mutate(c => c.Fill(SixLabors.ImageSharp.Color.ParseHex(properties.Config.BackgroundColor), yourPolygon));
                    SixLabors.Fonts.FontFamily family;
                    if (config.FontFamily == "微软雅黑")
                    {
                        family = SixLabors.Fonts.SystemFonts.Get("Microsoft YaHei");
                    }
                    else
                    {
                        byte[] bt = Global.FontResourrce[config.FontFamily];
                        using (var ms = new MemoryStream(bt))
                        {
                            var collection = new FontCollection();
                            family = collection.Add(ms);
                        }
                    }
                    //var font = new Font(fo, 1350, SixLabors.Fonts.FontStyle.Regular);

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
    }
}