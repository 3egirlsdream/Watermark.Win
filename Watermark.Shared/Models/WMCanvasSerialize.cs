using Watermark.Shared.Enums;

namespace Watermark.Win.Models
{
    public class WMCanvasSerialize
    {
        public WMCanvasSerialize()
        {
        }

        public string ID { get; set; }
        public string Name { get; set; }
        public WMThickness BorderThickness { get; set; }
        public string BackgroundColor { get; set; }
        public WMImage ImageProperties { get; set; }
        public bool EnableMarginXS { get; set; }

        public List<WMLine> Lines { get; set; }
        public List<WMLogo> Logos { get; set; }
        public List<WMText> Texts { get; set; }
        public List<WMContainer> Containers { get; set; }
		public CanvasType CanvasType { get; set; }
		public int CustomWidth { get; set; }
		public int CustomHeight { get; set; }
		public string LengthWidthRatio { get; set; }
	}

}
