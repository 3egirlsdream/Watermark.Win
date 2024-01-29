using Newtonsoft.Json;

namespace Watermark.Win.Models
{
    public class WMCanvas
    {
        public WMCanvas()
        {
            Children = [];
            ID = Guid.NewGuid().ToString("N").ToUpper();
            EnableMarginXS = false;
            BorderThickness = new WMThickness(0);
            ImageProperties = new WMImage();
        }
        public string ID { get; set; }
        public string Name { get; set; }
        public WMThickness BorderThickness { get; set; }
        public string BackgroundColor { get; set; } = "#FFF";
        public WMImage ImageProperties { get; set; }
        public List<WMContainer> Children { get; set; }
        [JsonIgnore]
        public Dictionary<string, string> Exif { get; set; }
        public bool EnableMarginXS { get; set; }
        [JsonIgnore]
        public string Path { get; set; }
    }

}
