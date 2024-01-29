using Newtonsoft.Json;

namespace Watermark.Win.Models
{
    public class WMText : IWMControl
    {
        public WMText()
        {
            ID = Guid.NewGuid().ToString("N").ToUpper();
            Exifs = [];
        }
        [JsonIgnore]
        public string Text { get; set; }
        public int FontSize { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public string FontColor { get; set; } = "#000";
        public string FontFamily { get; set; } = "微软雅黑";
        public List<WMExifConfigInfo> Exifs { get; set; }
    }

}
