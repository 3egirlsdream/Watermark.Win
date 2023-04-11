using System.Collections.ObjectModel;

namespace JointWatermark.Class
{
    public class LeftTextList : ValidationBase
    {
        public LeftTextList()
        {

        }

        public LeftTextList(string text, ObservableCollection<ExifConfigInfo> config)
        {
            Text = text;
            Config = config;
        }

        public string Text { get; set; } = "";

        private ObservableCollection<ExifConfigInfo> config = new();
        public ObservableCollection<ExifConfigInfo> Config
        {
            get => config;
            set
            {
                config = value;
                NotifyPropertyChanged(nameof(Config));
            }
        }
    }
}
