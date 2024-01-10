﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Watermark.Win.Models
{
    public class WMContainer
    {
        public WMContainer() 
        {
            Controls = [];
        }
        public Orientation Orientation { get; set; }    
        public HorizontalAlignment HorizontalAlignment {  get; set; }
        public VerticalAlignment VerticalAlignment { get; set; }
        public ContainerAlignment ContainerAlignment { get; set; }
        public Thickness Margin { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
        public List<IWMControl> Controls { get; set; }
    }


    public class WMCanvas
    {
        public WMCanvas()
        {
            Children = [];
        }
        public Thickness BorderThickness { get; set; }
        public string BackgroundColor { get; set; } = "#FFF";
        public List<WMContainer> Children { get; set; }
    }

    public class WMImage
    {
        public bool EnableShadow { get; set; }
        public bool EnableRadius { get; set; }
        public string Path { get; set; }
    }


    public interface IWMControl
    {
        public Thickness Margin { get; set; }
        public double Percent {  get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public class WMLogo : IWMControl
    {
        public Thickness Margin { get ; set ; }
        public double Percent { get; set; }
        public string Path { get; set; }
        public bool White2Transparent { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public class WMText : IWMControl
    {
        public Thickness Margin { get; set; }
        public double Percent { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Text { get; set; }
        public int FontSize { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public string FontColor { get; set; } = "#000";
        public string FontFamily { get; set; } = "微软雅黑";
    }

    public class WMLine: IWMControl
    {
        public Thickness Margin { get; set; }
        public double Percent { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public Orientation Orientation { get; set; }
        public int Thickness {  get; set; }
        public string Color { get; set; }
    }


    public enum ContainerAlignment
    {
        Left,
        Top, 
        Right, 
        Bottom
    }



}