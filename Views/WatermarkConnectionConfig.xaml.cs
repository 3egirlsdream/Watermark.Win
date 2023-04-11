using JointWatermark.Class;
using JointWatermark.Enums;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace JointWatermark.Views
{
    /// <summary>
    /// WatermarkConnectionConfig.xaml 的交互逻辑
    /// </summary>
    public partial class WatermarkConnectionConfig : Window
    {
        WatermarkConnectionConfigVM vm;
        public ConnectionMode row { get; set; }
        public WatermarkConnectionConfig(ConnectionMode _row)
        {
            InitializeComponent();
            row = _row;
            vm = new WatermarkConnectionConfigVM(this);
            DataContext = vm;
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if(sender is CheckBox check && check.DataContext is ExifInfo exif)
            {
                ExifConfigInfo item = new()
                {
                    SEQ = vm.Config.Count + 1,
                    Key = exif.Key,
                    Value = exif.Value
                };
                if (!vm.Config.Any(c => c.Key == item.Key))
                {
                    vm.Config.Add(item);
                }
            }
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox check && check.DataContext is ExifInfo exif)
            {
                var item = vm.Config.FirstOrDefault(c => c.Key == exif.Key);
                if(item!= null)
                {
                    vm.Config.Remove(item);
                }
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            vm.SaveConfig();
            this.DialogResult = true;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
        }

        private void fontlist_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && vm != null)
            {
                vm.FontFamily = combo.SelectedItem.ToString();
            }
        }

        private void togle1_Click(object sender, RoutedEventArgs e)
        {

        }
    }

    public class WatermarkConnectionConfigVM : ValidationBase
    {
        WatermarkConnectionConfig? window;
        public WatermarkConnectionConfigVM(Window _window)
        {
            window = _window as WatermarkConnectionConfig;
            var model = Global.InitConfig();
            ExifInfoList = new ObservableCollection<ExifInfo>(model.Exifs);
            InitData();
        }

        #region properties

        private ObservableCollection<ExifConfigInfo> config = new();
        public ObservableCollection<ExifConfigInfo> Config
        {
            get => config;
            set
            {
                config = value;
                NotifyPropertyChanged(nameof(Config));
                foreach (var item in ExifInfoList)
                {
                    if (Config.Any(c => c.Key == item.Key)) item.IsSelected = true;
                    else item.IsSelected = false;
                }
            }
        }

        private ObservableCollection<ExifInfo> exifInfoList = new();
        /// <summary>
        /// 全部的的EXIF配置信息
        /// </summary>
        public ObservableCollection<ExifInfo> ExifInfoList
        {
            get=> exifInfoList;
            set
            {
                exifInfoList = value;
                NotifyPropertyChanged(nameof(exifInfoList));
            }
        }

        private string fontColor;
        public string FontColor
        {
            get=> fontColor;
            set
            {
                fontColor = value;
                NotifyPropertyChanged(nameof(FontColor));
            }
        }

        private double fontZoom;
        public double FontZoom
        {
            get=> fontZoom;
            set
            {
                fontZoom = value;
                NotifyPropertyChanged(nameof(FontZoom));
            }
        }


        private int rowHeight;
        public int RowHeight
        {
            get => rowHeight;
            set
            {
                rowHeight = value;
                NotifyPropertyChanged(nameof(RowHeight));
            }
        }

        private string fontFamily;
        public string FontFamily
        {
            get=> fontFamily;
            set
            {
                fontFamily = value;
                NotifyPropertyChanged(nameof(FontFamily));
            }
        }

        private bool isBold;
        public bool IsBold
        {
            get=> isBold;
            set
            {
                isBold = value;
                NotifyPropertyChanged(nameof(IsBold));
            }
        }

        private string edgeWidth;
        public string EdgeWidth
        {
            get=> edgeWidth;
            set
            {
                if (window.edgeComputeMode.SelectedIndex != 0)
                {
                    var val = 0;
                    if (int.TryParse(value, out val) && val < 20 && val >= 0)
                    {
                        edgeWidth = val + "";
                    }
                }
                else
                {
                    edgeWidth = value;
                }
                NotifyPropertyChanged(nameof(EdgeWidth));
            }
        }

        #endregion
        #region function

        private void InitData()
        {
            RowHeight = (int)(window.row.RowHeightMinFontPercent ?? 100);
            window.edgeComputeMode.SelectedIndex = window.row.EdgeDistanceType == EdgeDistanceType.Character ? 0 : 1;
            EdgeWidth = window.row.EdgeDistanceType == EdgeDistanceType.Character ? window.row.EdgeDistanceCharacterX : window.row.EdgeDistancePercent + "";
        }

        public void SaveConfig()
        {
            window.row.RowHeightMinFontPercent = RowHeight;
            
            if (window.edgeComputeMode.SelectedIndex == 0)
            {
                window.row.EdgeDistanceType = EdgeDistanceType.Character;
                window.row.EdgeDistanceCharacterX = EdgeWidth;
            }
            else
            {
                int val;
                if(int.TryParse(EdgeWidth, out val))
                {
                    window.row.EdgeDistanceType = EdgeDistanceType.Percent;
                    window.row.EdgeDistancePercent = val;
                }
                
            }
        }


        #endregion

    }
}
