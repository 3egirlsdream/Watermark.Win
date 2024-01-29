using Newtonsoft.Json;

namespace Watermark.Win.Models
{
    public class IWMControl
    {
        public IWMControl()
        {
            Margin = new WMThickness(0);
        }
        public WMPNode PNode {  get; set; }
        public string Name { get; set; }
        public string ID { get; set; }
        public WMThickness Margin { get; set; }
        public double Percent { get; set; }
        [JsonIgnore]
        public double Width { get; set; }
        [JsonIgnore]
        public double Height { get; set; }
    }

}
