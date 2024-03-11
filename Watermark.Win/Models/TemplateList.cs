using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watermark.Win.Models
{
    public class TemplateList
    {
        public bool IsChecked { get; set; }
        public string ID { get; set; }
        public string Path { get; set; }
        public WMCanvas Canvas { get; set; }
        public string Src { get; set; }
    }
}
