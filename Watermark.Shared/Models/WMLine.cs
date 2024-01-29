using Watermark.Shared.Enums;

namespace Watermark.Win.Models
{
    public class WMLine : IWMControl
    {
        public WMLine()
        {
            ID = Guid.NewGuid().ToString("N").ToUpper();
            Color = "#000";
        }
        public Orientation Orientation { get; set; }
        public int Thickness { get; set; }
        public string Color { get; set; }
    }

}
