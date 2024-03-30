using Watermark.Shared.Enums;

namespace Watermark.Win.Models
{
    public class WMContainer : IWMControl
    {
        public WMContainer()
        {
            Controls = [];
            ID = Guid.NewGuid().ToString("N").ToUpper();
            HeightPercent = 13;
            WidthPercent = 100;
            Orientation = Orientation.Vertical;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment  = VerticalAlignment.Center;
            ContainerAlignment = ContainerAlignment.Bottom;
            XOffset = 0;
            YOffset = 0;
            ContainerProperties = new WMImage();
        }
        public Orientation Orientation { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; }
        public VerticalAlignment VerticalAlignment { get; set; }
        public ContainerAlignment ContainerAlignment { get; set; }
        public int HeightPercent { get; set; }
        public int WidthPercent { get; set; }
        public List<IWMControl> Controls { get; set; }
        public string Path { get; set; }
        /// <summary>
        /// 裁切
        /// </summary>
        public bool EnableCrop {  get; set; }
        public int XOffset {  get; set; }
        public int YOffset { get; set; }
        public WMImage ContainerProperties { get; set; }
    }

}
