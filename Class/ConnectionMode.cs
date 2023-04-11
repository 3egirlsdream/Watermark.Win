using SixLabors.ImageSharp;
using System.Collections.Generic;

namespace JointWatermark.Class
{
    /// <summary>
    /// 关联模式
    /// </summary>
    public class ConnectionMode : WatermarkProperty
    {
        public ConnectionMode()
        {
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

    }
}
