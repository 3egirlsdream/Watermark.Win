using JointWatermark.Enums;

namespace JointWatermark.Class
{
    public class WatermarkProperty : ValidationBase
    {

        private bool isChecked = true;

        /// <summary>
        /// 固定字符计数
        /// </summary>
        public string EdgeDistanceCharacterX { get; set; } = "";
        public string EdgeDistanceCharacterY { get; set; }

        /// <summary>
        /// 固定像素
        /// </summary>
        public int EdgeDistanceFixedPixel { get; set; }

        /// <summary>
        /// 百分比计数
        /// </summary>
        public int EdgeDistancePercent { get; set; }
        /// <summary>
        /// 边距计算方式
        /// </summary>
        public EdgeDistanceType EdgeDistanceType { get; set; }
        public WatermarkRange End { get; set; }

        /// <summary>
        /// 相对定位方式，根据上一行或者全局
        /// </summary>
        public RelativePositionMode RelativePositionMode { get; set; } = RelativePositionMode.Global;
        public WatermarkRange Start { get; set; }

        //每个水印位置的偏移量->支持百分比和像素2种


        /// <summary>
        /// 水印的定位基准
        /// </summary>
        public PositionBase X { get; set; }
        public PositionBase Y { get; set; }
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
    }
}