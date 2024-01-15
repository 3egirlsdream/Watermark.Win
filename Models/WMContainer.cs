using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Watermark.Win.Models
{
    public class WMContainer : IWMControl
    {
        public WMContainer()
        {
            Controls = [];
            ID = Guid.NewGuid().ToString("N").ToUpper();
            HeightPercent = 1;
            WidthPercent = 1;
        }
        public Orientation Orientation { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; }
        public VerticalAlignment VerticalAlignment { get; set; }
        public ContainerAlignment ContainerAlignment { get; set; }
        public int HeightPercent { get; set; }
        public int WidthPercent { get; set; }
        public List<IWMControl> Controls { get; set; }
    }

    public class WMCanvasSerialize
    {
        public WMCanvasSerialize()
        {
        }

        public string ID { get; set; }
        public string Name { get; set; }
        public Thickness BorderThickness { get; set; }
        public string BackgroundColor { get; set; }
        public WMImage ImageProperties { get; set; }
        public bool EnableMarginXS { get; set; }

        public List<WMLine> Lines { get; set; }
        public List<WMLogo> Logos { get; set; }
        public List<WMText> Texts { get; set; }
        public List<WMContainer> Containers { get; set; }
    }


    public class WMCanvas
    {
        public WMCanvas()
        {
            Children = [];
            ID = Guid.NewGuid().ToString("N").ToUpper();
            EnableMarginXS = false;
            BorderThickness = new Thickness(0);
            ImageProperties = new WMImage();
        }
        public string ID { get; set; }
        public string Name { get; set; }
        public Thickness BorderThickness { get; set; }
        public string BackgroundColor { get; set; } = "#FFF";
        public WMImage ImageProperties { get; set; }
        public List<WMContainer> Children { get; set; }
        [JsonIgnore]
        public Dictionary<string, string> Exif { get; set; }
        public bool EnableMarginXS { get; set; }
        [JsonIgnore]
        public string Path { get; set; }
    }

    public class WMImage
    {
        public WMImage()
        {
            EnableRadius = false;
            EnableShadow = false;
            ShadowRange = 10;
            ShadowColor = "#FF808080";
            CornerRadius = 15;
        }
        public bool EnableShadow { get; set; }
        public int ShadowRange {  get; set; }
        public string ShadowColor { get; set; }
        public bool EnableRadius { get; set; }
        public int CornerRadius { get; set; }
    }

    public class PNode
    {
        public PNode(int seq, string id) 
        { 
            SEQ = seq;
            PID = id;
        }
        public int SEQ { get; set; }
        public string PID {  get; set; }
    }

    public class IWMControl
    {
        public IWMControl()
        {
            Margin = new Thickness(0);
        }
        public PNode PNode {  get; set; }
        public string Name { get; set; }
        public string ID { get; set; }
        public Thickness Margin { get; set; }
        public double Percent { get; set; }
        [JsonIgnore]
        public double Width { get; set; }
        [JsonIgnore]
        public double Height { get; set; }
    }


    public class WMLogo : IWMControl
    {
        public WMLogo()
        {
            ID = Guid.NewGuid().ToString("N").ToUpper();
        }
        public string Path { get; set; }
        public bool White2Transparent { get; set; }
    }

    public class WMText : IWMControl
    {
        public WMText()
        {
            ID = Guid.NewGuid().ToString("N").ToUpper();
            Exifs = new List<ExifConfigInfo>();
        }
        [JsonIgnore]
        public string Text { get; set; }
        public int FontSize { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public string FontColor { get; set; } = "#000";
        public string FontFamily { get; set; } = "微软雅黑";
        public List<ExifConfigInfo> Exifs { get; set; }
    }

    public class WMLine : IWMControl
    {
        public WMLine()
        {
            ID = Guid.NewGuid().ToString("N").ToUpper();
        }
        public Orientation Orientation { get; set; }
        public int Thickness { get; set; }
        public string Color { get; set; }
    }

    public class ExifConfigInfo
    {
        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public string Key { get; set; }

        public string Value { get; set; }
    }


    public enum ContainerAlignment
    {
        Left,
        Top,
        Right,
        Bottom
    }
    public class Thickness
    {
        public Thickness() { }
        public Thickness(double v)
        {
            Bottom = Left = Top = Right = v;
        }
        public Thickness(double left, double top, double right, double bottom)
        {
            Bottom = bottom;
            Left = left;
            Right = right;
            Top = top;
        }

        public double Bottom { get; set; }
        public double Left { get; set; }
        public double Right { get; set; }
        public double Top { get; set; }

    }

}
