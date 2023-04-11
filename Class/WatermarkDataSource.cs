using JointWatermark.Enums;
using System.Collections.Generic;

namespace JointWatermark.Class
{
    public class WatermarkDataSource
    {
        public DataSourceFrom From { get; set; }
        public List<ExifConfigInfo> Exifs { get; set; }
    }
}
