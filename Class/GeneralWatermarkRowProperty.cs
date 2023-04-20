using JointWatermark.Enums;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace JointWatermark.Class
{
    /// <summary>
    /// 水印模板行
    /// </summary>
    public class GeneralWatermarkRowProperty : WatermarkProperty
    {
        public GeneralWatermarkRowProperty()
        {
            ID = Guid.NewGuid().ToString("N").ToUpper();
            DataSource = new WatermarkDataSource();
            ImagePath = new Photo();
            DateFormat = new List<string> { ".", ".", ":", ":" };
        }

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
        public string FontFamily { set; get; } = "OpenSans";
        public bool IsBold { get; set; } = false;
        public int FontSize { get; set; } = 30;
        public string Color { get; set; } = "#000000";
        public List<string> DateFormat { get; set; }
        public double FontXS { get; set; } = 1;



        /**
         * 
         */
        [JsonIgnore]
        public System.Windows.Visibility Visibility
        {
            get => ContentType == ContentType.Text ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            set { }
        }
    }
}
