using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JointWatermark.Class
{
    public class ImageCornerRound
    {
        public ImageCornerRound() { }

        public ImageCornerRound(bool enabled, int cornerRadius) {
            Enabled = enabled;
            CornerRadius = cornerRadius;
        }
        public bool Enabled {  get; set; }
        public int CornerRadius { get; set; } = 100;
    }
}
