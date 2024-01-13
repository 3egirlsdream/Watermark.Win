using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Watermark.Win.Models
{
    public class WatermarkHelper
    {
        public string Generation(WMCanvas mainCanvas)
        {
            string path = Global.TemplatesFolder + mainCanvas.ID + Path.DirectorySeparatorChar + "default.jpg";// "C:\\Users\\Jiang\\Pictures\\DSC02754.jpg";
            if(!string.IsNullOrEmpty(mainCanvas.Path) ) 
            {
                path = mainCanvas.Path;
            }
            var originalBitmap = SKBitmap.Decode(path);
            if(originalBitmap == null)
            {
                return "";
            }
            var meta = ExifHelper.ReadImage(path);
            mainCanvas.Exif = meta;
            // var originalImage = SKImage.FromBitmap(originalBitmap);
            var xs = (originalBitmap.Height * originalBitmap.Width) / (1080.0 * 1980);
            //创建画布
            var wh_xs = Math.Min(originalBitmap.Width, originalBitmap.Height) * 1.0 / Math.Max(originalBitmap.Width, originalBitmap.Height);
            var singeBorderWidth = originalBitmap.Width / 100.0;
            var singeBorderHeight = originalBitmap.Height / 100.0;
            double sw = singeBorderWidth, sh = singeBorderHeight;
            if (mainCanvas.EnableMarginXS)
            {
                if (sh > sw) sh *= wh_xs;
                else sw *= wh_xs;
            }
            var border_l = sw * mainCanvas.BorderThickness.Left;
            var border_r = sw * mainCanvas.BorderThickness.Right;
            var border_b = sh * mainCanvas.BorderThickness.Bottom;
            var border_t = sh * mainCanvas.BorderThickness.Top;

            var totalHeight = originalBitmap.Height + border_b + border_t;
            var totalWidth = originalBitmap.Width + border_l + border_r;
            var info = new SKImageInfo()
            {
                Height = (int)totalHeight,
                Width = (int)totalWidth,
                AlphaType = originalBitmap.AlphaType,
                ColorSpace = originalBitmap.ColorSpace,
                ColorType = originalBitmap.ColorType
            };
            var image1 = SKImage.Create(info);
            var bitmap1 = SKBitmap.FromImage(image1);
            SKCanvas canvas1 = new SKCanvas(bitmap1);
            canvas1.Clear(SKColor.Parse(mainCanvas.BackgroundColor));
            SKPoint p1 = new SKPoint((float)border_l, (float)border_t);
            canvas1.DrawBitmap(originalBitmap, p1);

            var paint = new SKPaint
            {
                TextSize = 32,
                Color = SKColors.Black
            };

            //var phh = paint.FontMetrics.Descent - paint.FontMetrics.Ascent;
            //canvas1.DrawText("Hello World!", new SKPoint(0, phh), paint);


            //绘制容器
            foreach (var container in mainCanvas.Children)
            {
                SKBitmap bitmapc = DrawContainer(meta, originalBitmap, xs, ref info, container);

                var container_point = new SKPoint(0, 0);
                var cl = container.Margin.Left * singeBorderWidth;
                var cr = container.Margin.Right * singeBorderWidth;
                var ct = container.Margin.Top * singeBorderHeight;
                var cb = container.Margin.Bottom * singeBorderHeight;
                //绘制容器的位置
                if (container.ContainerAlignment == ContainerAlignment.Top)
                {
                    container_point = new SKPoint((float)(cl+border_l), (float)ct);
                }
                else if (container.ContainerAlignment == ContainerAlignment.Left)
                {
                    container_point = new SKPoint((float)(cl), (float)(ct+border_t));
                }
                else if (container.ContainerAlignment == ContainerAlignment.Right)
                {
                    var cw = container.WidthPercent * singeBorderWidth;
                    var r = (float)(totalWidth - cr - cw);
                    container_point = new SKPoint(r, (float)(ct + border_t));
                }
                else if (container.ContainerAlignment == ContainerAlignment.Bottom)
                {
                    var ch = container.HeightPercent  * singeBorderHeight;
                    var b = (totalHeight - ch - cb);
                    container_point = new SKPoint((float)(cl+border_l), (float)b);
                }
                canvas1.DrawBitmap(bitmapc, container_point);
            }

            using var sk = SKImage.FromBitmap(bitmap1);
            using var data = sk.Encode(SKEncodedImageFormat.Jpeg, 100);
            //using var sm = File.OpenWrite("output.jpg");
            //data.SaveTo(sm);
            var bytes = data.ToArray();
            
            return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
        }

        private SKBitmap DrawContainer(Dictionary<string, string> meta, SKBitmap originalBitmap, double xs, ref SKImageInfo info, WMContainer container)
        {
            //创建容器大小的画布
            var hc = container.HeightPercent / 100.0 * originalBitmap.Height;
            var wc = container.WidthPercent / 100.0 * originalBitmap.Width;
            info.Height = (int)hc;
            info.Width = (int)wc;
            var bitmapc = new SKBitmap(info.Width, info.Height);
            var canvasc = new SKCanvas(bitmapc);
            canvasc.Clear(SKColors.Transparent);

            void DrawLogo(double hc, double wc, IWMControl component, WMLogo mLogo, out SKCanvas canvas_cp, out SKBitmap bitmap_logo, Action<SKBitmap> callback)
            {
                //logo系数按窄边计算
                var min = Math.Min(hc, wc);
                bitmap_logo = SKBitmap.Decode(mLogo.Path);
                if (mLogo.White2Transparent)
                {
                    bitmap_logo = ConvertWhiteToTransparent(bitmap_logo);
                }
                var hcp = min * (component.Percent / 100.0);

                var logo_xs = hcp * 1.0 / Math.Min(bitmap_logo.Width, bitmap_logo.Height);

                var wcp = bitmap_logo.Width * logo_xs;
                hcp = bitmap_logo.Height * logo_xs;
                var bitmap_cp = new SKBitmap((int)wcp, (int)hcp);
                canvas_cp = new SKCanvas(bitmap_cp);
                var rect_cp = new SKRect(0, 0, (int)wcp, (int)hcp);
                canvas_cp.DrawBitmap(bitmap_logo, rect_cp);

                //记录图表尺寸
                mLogo.Width = wcp;
                mLogo.Height = hcp;
                callback?.Invoke(bitmap_cp);
            }

            void DrawText(double xs, WMText mText, Action<SKPaint> action)
            {
                SKFontStyle fontStyle;
                if (mText.IsBold && mText.IsItalic) fontStyle = SKFontStyle.BoldItalic;
                else if (mText.IsItalic) fontStyle = SKFontStyle.Italic;
                else if (mText.IsBold) fontStyle = SKFontStyle.Bold;
                else fontStyle = SKFontStyle.Normal;

                var typeface_cp = SKTypeface.FromFamilyName(mText.FontFamily, fontStyle);


                var fontxs = Math.Min(hc, wc) / 156.0;
                if(fontxs == 0) fontxs = 1;


                //字体乘以系数
                var paint_cp = new SKPaint()
                {
                    Color = SKColor.Parse(mText.FontColor),
                    TextSize = (int)(mText.FontSize * fontxs),
                    Typeface = typeface_cp
                };
                var text = string.Join(" ",
                            mText.Exifs.Select(x =>
                            {
                                if (meta.TryGetValue(x.Key, out var value))
                                {
                                    return x.Prefix + value + x.Suffix;
                                }
                                return x.Prefix + x.Suffix;
                            }));
                var fw = paint_cp.MeasureText(text);
                var fh = paint_cp.FontMetrics.Descent - paint_cp.FontMetrics.Ascent;
                mText.Height = fh;
                mText.Width = fw;

                action?.Invoke(paint_cp);
            }


            //首先计算出所有组件占据的宽高
            foreach (var component in container.Controls)
            {
                if (component is WMLogo mLogo)
                {
                    SKCanvas canvas_cp;
                    SKBitmap bitmap_logo;
                    DrawLogo(hc, wc, component, mLogo, out canvas_cp, out bitmap_logo, null);
                }
                else if (component is WMText mText)
                {
                    DrawText(xs, mText, null);
                }
                else if (component is WMLine mLine)
                {
                    if (mLine.Orientation == System.Windows.Controls.Orientation.Horizontal)
                    {
                        mLine.Height = mLine.Thickness * xs;
                        mLine.Width = component.Percent / 100.0 * wc;
                    }
                    else
                    {
                        mLine.Height = component.Percent / 100.0 * hc;
                        mLine.Width = mLine.Thickness * xs;
                    }
                }
                else if (component is WMContainer mContainer)
                {
                    var bitmap_child_c = DrawContainer(meta, bitmapc, xs, ref info, mContainer);
                    mContainer.Height = bitmap_child_c.Height;
                    mContainer.Width = bitmap_child_c.Width;
                }
            }



            //被前面组件占用了的地方
            double occupy_x = 0, occupy_y = 0;
            //计算组件实际的坐标
            foreach (var component in container.Controls)
            {
                double stdx = 0, stdy = 0;
                //水平布局，比例按高计算, margin只左右生效
                if (container.Orientation == System.Windows.Controls.Orientation.Horizontal)
                {
                    var ch = container.HeightPercent / 100.0 * originalBitmap.Height;
                    var cw = container.WidthPercent / 100.0 * originalBitmap.Width;
                    if (container.VerticalAlignment == System.Windows.VerticalAlignment.Top)
                    {
                        stdy = 0;
                    }
                    else if (container.VerticalAlignment == System.Windows.VerticalAlignment.Center)
                    {
                        stdy = (ch - component.Height) / 2;
                    }
                    else if (container.VerticalAlignment == System.Windows.VerticalAlignment.Bottom)
                    {
                        stdy = ch - component.Height;
                    }
                    stdy += (component.Margin.Top - component.Margin.Bottom) / 100.0 * ch;

                    if (container.HorizontalAlignment == System.Windows.HorizontalAlignment.Left)
                    {
                        stdx = occupy_x + (ch * (component.Margin.Left - component.Margin.Right) / 100.0);
                        occupy_x = stdx + component.Width;
                    }
                    else if (container.HorizontalAlignment == System.Windows.HorizontalAlignment.Center)
                    {
                        if (occupy_x == 0)
                        {
                            var totalComponentWidth = container.Controls.Sum(c => c.Width) + container.Controls.Select(c => (c.Margin.Left + c.Margin.Right) / 100.0 * container.HeightPercent).Sum();
                            occupy_x = (cw - totalComponentWidth) / 2;
                        }
                        stdx = occupy_x + (ch * (component.Margin.Left - component.Margin.Right) / 100.0);
                        occupy_x = stdx + component.Width;
                    }
                    else if (container.HorizontalAlignment == System.Windows.HorizontalAlignment.Right)
                    {
                        if (occupy_x == 0)
                        {
                            occupy_x = cw;
                        }
                        stdx = occupy_x - component.Width - (ch * (component.Margin.Right - component.Margin.Left) / 100.0);
                        occupy_x = stdx;
                    }
                }
                else
                {
                    var ch = container.HeightPercent / 100.0 * originalBitmap.Height;
                    var cw = container.WidthPercent / 100.0 * originalBitmap.Width;
                    if (container.HorizontalAlignment == System.Windows.HorizontalAlignment.Left)
                    {
                        stdx = 0;
                    }
                    else if (container.HorizontalAlignment == System.Windows.HorizontalAlignment.Center)
                    {
                        stdx = (cw - component.Width) / 2;
                    }
                    else if (container.HorizontalAlignment == System.Windows.HorizontalAlignment.Right)
                    {
                        stdx = cw - component.Width;
                    }

                    stdx += (component.Margin.Left - component.Margin.Right) / 100.0 * cw;


                    if (container.VerticalAlignment == System.Windows.VerticalAlignment.Top)
                    {
                        stdy = 0;
                        occupy_y = stdy + component.Height + (ch * (component.Margin.Top - component.Margin.Bottom) / 100.0);
                    }
                    else if (container.VerticalAlignment == System.Windows.VerticalAlignment.Center)
                    {
                        var min = Math.Min(hc, wc);
                        if (occupy_y == 0)
                        {
                            var totalComponentHeight = container.Controls.Sum(c => c.Height) + container.Controls.Select(c => (c.Margin.Top + c.Margin.Bottom) / 100.0 * min).Sum();
                            occupy_y = (ch - totalComponentHeight) / 2;
                        }
                        stdy = occupy_y;
                        occupy_y = stdy + component.Height + (min * (component.Margin.Top - component.Margin.Bottom) / 100.0);
                    }
                    else if (container.VerticalAlignment == System.Windows.VerticalAlignment.Bottom)
                    {
                        if (occupy_y == 0)
                        {
                            occupy_y = ch;
                        }
                        stdy = occupy_y - component.Height - (ch * (component.Margin.Bottom - component.Margin.Top) / 100.0);
                        occupy_y = stdy;
                    }


                }

                //绘制
                if (component is WMLogo mLogo)
                {
                    SKCanvas canvas_cp;
                    SKBitmap bitmap_logo;
                    var action = new Action<SKBitmap>((bitmap_cp) =>
                    {
                        canvasc.DrawBitmap(bitmap_cp, new SKPoint((float)stdx, (float)stdy));
                    });
                    DrawLogo(hc, wc, component, mLogo, out canvas_cp, out bitmap_logo, action);
                }
                else if (component is WMText mText)
                {
                    var action = new Action<SKPaint>((p) =>
                    {
                        var skp = new SKPoint((float)stdx, (float)(stdy + mText.Height));
                        var text = string.Join(" ",
                            mText.Exifs.Select(x =>
                            {
                                if (meta.TryGetValue(x.Key, out var value))
                                {
                                    return x.Prefix + value + x.Suffix;
                                }
                                return x.Prefix + x.Suffix;
                            }));
                        canvasc.DrawText(text, skp, p);
                    });
                    DrawText(xs, mText, action);
                }
                else if (component is WMLine mLine)
                {
                    var pt1 = new SKPoint();
                    var pt2 = new SKPoint();
                    var paint_line = new SKPaint
                    {
                        Color = SKColor.Parse(mLine.Color),
                        StrokeWidth = (float)Math.Min(mLine.Height, mLine.Width)
                    };
                    var maxLine = Math.Max(mLine.Height, mLine.Width);
                    if (mLine.Orientation == System.Windows.Controls.Orientation.Horizontal)
                    {
                        pt1 = new SKPoint((float)stdx, (float)stdy);
                        pt2 = new SKPoint((float)(pt1.X + maxLine), pt1.Y);
                    }
                    else
                    {
                        pt1 = new SKPoint((float)stdx, (float)stdy);
                        pt2 = new SKPoint(pt1.X, (float)(pt1.Y + maxLine));
                    }
                    canvasc.DrawLine(pt1, pt2, paint_line);
                }
                else if (component is WMContainer mContainer)
                {
                    var bitmap_child_c = DrawContainer(meta, bitmapc, xs, ref info, mContainer);
                    var child_cp_pt = new SKPoint((float)stdx, (float)stdy);
                    canvasc.DrawBitmap(bitmap_child_c, child_cp_pt);
                }
            }

            return bitmapc;
        }

        // 将白色像素转为透明像素
        static SKBitmap ConvertWhiteToTransparent(SKBitmap originalBitmap)
        {
            var modifiedBitmap = new SKBitmap(originalBitmap.Width, originalBitmap.Height);

            for (int x = 0; x < originalBitmap.Width; x++)
            {
                for (int y = 0; y < originalBitmap.Height; y++)
                {
                    var pixelColor = originalBitmap.GetPixel(x, y);

                    if (pixelColor.Red == 255 && pixelColor.Green == 255 && pixelColor.Blue == 255)
                    {
                        //pixelColor.Alpha = 0;
                        var pc = new SKColor(255, 255, 255, 0);
                        modifiedBitmap.SetPixel(x, y, pc);
                    }
                    else
                    {
                        modifiedBitmap.SetPixel(x, y, pixelColor);
                    }
                }
            }
            return modifiedBitmap;

        }
    }
}
