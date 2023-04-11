using JointWatermark.Class;
using JointWatermark.Enums;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace JointWatermark.Views
{
    /// <summary>
    /// WatermarkRowConfig.xaml 的交互逻辑
    /// </summary>
    public partial class WatermarkRowConfig : Window
    {
        WatermarkRowConfigVM vm;
        public GeneralWatermarkRowProperty row { get; set; }
        public WatermarkRowConfig(GeneralWatermarkRowProperty _row)
        {
            InitializeComponent();
            row = _row;
            vm = new WatermarkRowConfigVM(this);
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


        private void fontlist_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && vm != null)
            {
                vm.FontFamily = combo.SelectedItem.ToString();
            }
        }

        private void togle1_Click(object sender, RoutedEventArgs e)
        {
            var dialog = ColorPickerWPF.ColorPickerWindow.ShowDialog(out System.Windows.Media.Color color, ColorPickerWPF.Code.ColorPickerDialogOptions.SimpleView);
            if (dialog == true)
            {
                System.Drawing.Color _c = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
                vm.FontColor = ColorTranslator.ToHtml(_c);
            }
        }
    }

    public class WatermarkRowConfigVM : ValidationBase
    {
        WatermarkRowConfig? window;
        public WatermarkRowConfigVM(Window _window)
        {
            window = _window as WatermarkRowConfig;
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
            FontColor = window.row.Color;
            FontZoom = window.row.FontXS;
            FontFamily = window.row.FontFamily;
            IsBold = window.row.IsBold;
            window.edgeComputeMode.SelectedIndex = window.row.EdgeDistanceType == EdgeDistanceType.Character ? 0 : 1;
            EdgeWidth = window.row.EdgeDistanceType == EdgeDistanceType.Character ? window.row.EdgeDistanceCharacterX : window.row.EdgeDistancePercent + "";
            var fontList = Global.InitFontList();
            window.fontlist.ItemsSource = fontList;
            var fontIdx = fontList.IndexOf(window.row.FontFamily);
            window.fontlist.SelectedIndex = fontIdx;
            window.yearMon.Text = window.row.DateFormat[0];
            window.monDay.Text = window.row.DateFormat[1];
            window.hourMin.Text = window.row.DateFormat[2];
            window.minSec.Text = window.row.DateFormat[3];
            if(window.row.DataSource != null && window.row.DataSource.Exifs != null)
            Config = new ObservableCollection<ExifConfigInfo>(window.row.DataSource.Exifs);
        }

        public void SaveConfig()
        {
            window.row.DataSource = new WatermarkDataSource
            {
                From = DataSourceFrom.Exif,
                Exifs = Config.ToList(),
            };
            window.row.Color = FontColor;
            window.row.DateFormat = new List<string> { window.yearMon.Text, window.monDay.Text, window.hourMin.Text, window.minSec.Text };
            window.row.FontXS = FontZoom;
            window.row.FontFamily = FontFamily;
            window.row.IsBold = IsBold;
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
