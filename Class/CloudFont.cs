using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JointWatermark.Class
{
    public class CloudFont : ValidationBase
    {
        public string ID { get; set; }
        public string NAME { get; set; }
        public string URL { get; set; }
        public string URL_B { get; set; }
        public int Progress { get; set; }
        private bool isLoading = false;
        public bool IsLoading
        {
            get => isLoading; 
            set
            {
                isLoading = value;
                NotifyPropertyChanged(nameof(IsLoading));
            }
        }
    }
}
