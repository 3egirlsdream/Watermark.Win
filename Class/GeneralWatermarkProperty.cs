using JointWatermark.Enums;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WeakToys.Class;



namespace JointWatermark.Class
{
    public partial class GeneralWatermarkProperty : ValidationBase
    {
        public string Name { get; set; }

        private bool isChecked;
        [JsonIgnore]
        public bool IsChecked
        {
            get => isChecked;
            set
            {
                isChecked = value;
                NotifyPropertyChanged(nameof(IsChecked));
            }
        }
    }
}

namespace JointWatermark.Class
{
    public partial class GeneralWatermarkProperty
    {
        public GeneralWatermarkProperty()
        {
            ID = Guid.NewGuid().ToString("N").ToUpper();
            CornerRound ??= new ImageCornerRound(false, 100);
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
        public string GlobalBkColor { get; set; }

        public ImageBackgroud ImageBackgroud { get; set; }


        /// <summary>
        /// 字体
        /// </summary>
        public string FontFamily { get; set; }

        public ImageShadow Shadow { get; set; }
        public bool WhiteToTransparent { get; set; }

        public List<GeneralWatermarkRowProperty> Properties { get; set; }

        public List<ConnectionMode> ConnectionModes { get; set; }
        [JsonIgnore]
        public Dictionary<string, object> Meta { get; set; }
        [JsonIgnore]
        public bool MetaEmpty { get; set; }



        /// <summary>
        /// 横纵比
        /// </summary>
        public string AspectRatio { get; set; } = "";

        public ImageCornerRound CornerRound { get; set; }

    }
}
