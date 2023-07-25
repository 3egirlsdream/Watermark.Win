using System.Collections.Generic;

namespace JointWatermark.Class
{
    public class MainModel
    {
        public List<string> SavePath = new List<string>();
        public int Quality { get; set; }
        public List<ExifInfo> Exifs { get; set; } = new List<ExifInfo>();

        public List<LeftTextList> Config { get; set; } = new List<LeftTextList>();

        public List<string> Icons { get; set; } = new List<string>();
        public bool ShowGuide { get; set; }
        public WatermarkTemplates Templates { get; set; }
    }
}
