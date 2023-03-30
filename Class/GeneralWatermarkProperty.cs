using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JointWatermark.Class
{
    public class GeneralWatermarkProperty : ValidationBase
    {
        /// <summary>
        /// 固定百分比，根据短边
        /// </summary>
        public bool EnableFixedPercent { get; set; }
        public string PhotoPath { get; set; }
        /// <summary>
        /// 图片比例：-1不限制。169，219 43 43 11
        /// </summary>
        public int PhotoRatio { get; set; }

        //图片的高度占整个图片的比例，通过设置图片比例，加起始位置比例实现边框效果
        
        /// <summary>
        /// 图片起始位置，按短边百分比计算
        /// </summary>
        public Point StartPosition { get; set; }
        /// <summary>
        /// 图片占整个图片的比例
        /// </summary>
        public int PecentOfWidth { get; set; }
        public int PecentOfHeight { get; set; }

        /// <summary>
        /// 图片方向
        /// </summary>
        public PhotoAlignment PhotoAlignment { get; set; }  
        /// <summary>
        /// 字体
        /// </summary>
        public string FontFamily { get; set; }

        public List<GeneralWatermarkRowProperty> Properties { get; set; }
        
    }

    /// <summary>
    /// 水印模板行
    /// </summary>
    public class GeneralWatermarkRowProperty : ValidationBase
    {

        //每个水印位置的偏移量->支持百分比和像素2种

        /// <summary>
        /// 水印的定位基准
        /// </summary>
        public PositionBase X { get; set; }
        public PositionBase Y { get; set; }
        /// <summary>
        /// 边距计算方式
        /// </summary>
        public EdgeDistanceType EdgeDistanceType { get; set; }
        /// <summary>
        /// 百分比计数
        /// </summary>
        public int EdgeDistancePercent { get; set; }
        /// <summary>
        /// 固定字符计数
        /// </summary>
        public string EdgeDistanceCharacterX { get; set; }
        public string EdgeDistanceCharacterY { get; set; }
        /// <summary>
        /// 相对定位方式，根据上一行或者全局
        /// </summary>
        public RelativePositionMode RelativePositionMode { get; set; }

        /// <summary>
        /// 水印内容
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// 字体
        /// </summary>
        public string FontFamily { set; get; } = "微软雅黑";
        public bool IsBlod { get; set; } = false;

        public int StartX { get; set; }
        public int EndX { get; set; }
        public int StartY { get; set; }
        public int EndY { get; set; }
    }

    public enum RelativePositionMode
    {
        LastRow,
        Global
    }

    public enum EdgeDistanceType
    {
        Percent,
        Character
    }

    public enum PositionBase
    {
        Left,
        Top,
        Right,
        Bottom,
        Center
    }

    public enum PhotoAlignment
    {
        Horizon,
        Verital
    }

    public enum Direction
    {
        Top,
        Bottom,
        Right,
        Left,
    }
}
