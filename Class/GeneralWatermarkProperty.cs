using JointWatermark.Views;
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
        public GeneralWatermarkProperty() 
        {
            ID = Guid.NewGuid().ToString("N").ToUpper();
        }

        public string ID { get; set; }
        /// <summary>
        /// 固定百分比，根据短边 // 主要是为了白色边框一样宽
        /// </summary>
        public bool EnableFixedPercent { get; set; }
        public string PhotoPath { get; set; }
        /// <summary>
        /// 缩略图
        /// </summary>
        public string ThumbnailPath { get; set; }
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

        public string BackgroundColor { get; set; } = "#FFFFFF";

        /// <summary>
        /// 字体
        /// </summary>
        public string FontFamily { get; set; }

        public ImageShadow Shadow { get; set; }

        public List<GeneralWatermarkRowProperty> Properties { get; set; }

        public List<ConnectionMode> ConnectionModes { get; set; }
        public Dictionary<string, object> Meta { get; set; }
        
    }

    public class ImageShadow
    {
        public ImageShadow() { }
        public ImageShadow(bool enabled, int width) 
        { 
            Enabled = enabled;
            Width = width;
        }
        public bool Enabled { get; set; }
        public int Width { get; set; } = 200;
    }

    /// <summary>
    /// 水印模板行
    /// </summary>
    public class GeneralWatermarkRowProperty : ValidationBase
    {
        public GeneralWatermarkRowProperty() 
        {
            ID = Guid.NewGuid().ToString("N").ToUpper();
            DataSource = new WatermarkDataSource();
            ImagePath = new Photo();
            DateFormat = new List<string> { ".", ".", ":", ":" };
        }

        private bool isChecked = true;
        public bool IsChecked
        {
            get => isChecked;
            set
            {
                isChecked = value;
                NotifyPropertyChanged(nameof(IsChecked));
            }
        }
        public string ID { get; set; }
        public string Name { get; set; }
        public WatermarkRange Start { get; set; }
        public WatermarkRange End { get; set; }

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
        /// 固定像素
        /// </summary>
        public int EdgeDistanceFixedPixel { get; set; } 
        /// <summary>
        /// 固定字符计数
        /// </summary>
        public string EdgeDistanceCharacterX { get; set; } = "";
        public string EdgeDistanceCharacterY { get; set; } = "";
        /// <summary>
        /// 相对定位方式，根据上一行或者全局
        /// </summary>
        public RelativePositionMode RelativePositionMode { get; set; } = RelativePositionMode.Global;

        /// <summary>
        /// 水印内容
        /// </summary>
        public string Content { get; set; } = "";

        public WatermarkDataSource DataSource { get; set; }

        /// <summary>
        /// 图片路径
        /// </summary>
        public Photo ImagePath { get; set; }

        public ContentType ContentType { get; set; } = ContentType.Text;

        /// <summary>   
        /// 图片占范围的比例（按短边缩放）
        /// </summary>
        public int ImagePercentOfRange { get; set; }
        /// <summary>
        /// 线占的百分比
        /// </summary>
        public int LinePercentOfRange { get; set; }
        /// <summary>
        /// 线占用的像素
        /// </summary>
        public int LinePixel { get; set; }

        /// <summary>
        /// 字体
        /// </summary>
        public string FontFamily { set; get; } = "微软雅黑";
        public bool IsBold { get; set; } = false;
        public int FontSize { get; set; } = 30;
        public string Color { get; set; } = "#000000";
        public List<string> DateFormat { get; set; }
        public double FontXS { get; set; } = 1;

        /**
         * 记录当前组件计算完后的值
         * 
         */

        /// <summary>
        /// 记录当前组件计算完后的值
        /// </summary>
        public double TotalWidth { get; set; }
        /// <summary>
        /// 记录当前组件计算完后的值
        /// </summary>
        public double TotalHeight { get; set; }
        /// <summary>
        /// 记录当前组件计算完后的值
        /// </summary>
        public Point StartPoint { get; set; }



        /**
         * 
         */
        public System.Windows.Visibility Visibility
        {
            get => ContentType == ContentType.Text ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            set { }
        }
    }


    /// <summary>
    /// 关联模式
    /// </summary>
    public class ConnectionMode : GeneralWatermarkRowProperty
    {
        public ConnectionMode() 
        {
            DateFormat = new List<string>();
        }
        public List<string> Ids { get; set; }
        /// <summary>
        /// 按整体图片百分比计数
        /// </summary>
        public double? RowHeightPercent { get; set; } = null;
        /// <summary>
        /// 按水印组的最小字体高度百分比计数
        /// </summary>
        public double? RowHeightMinFontPercent { get; set; } = null;

    }


    public class Photo
    {
        public Photo() { }
        public Photo(string path , bool isCloud, bool isLogo) 
        { 
            Path = path;
            IsCloud = isCloud;
            IsLogo = isLogo;
        }
        public string Path { get; set; }
        public bool IsCloud { get; set; }
        public bool IsLogo { get; set; }
    }

    public enum RelativePositionMode
    {
        LastRow,
        Global
    }

    public enum EdgeDistanceType
    {
        Percent,
        Character,
        Pixel
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

    public enum WatermarkRange
    {
        BottomOfPhoto,
        TopOfPhoto,
        LeftOfPhoto,
        RightOfPhoto,
        End,
        None
    }

    public enum ContentType
    {
        Image,
        Text,
        Line
    }
}
