using JointWatermark.Class;
using JointWatermark.Enums;
using Newtonsoft.Json;
using System;
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
        public Dictionary<string, object> Meta {  get; private set; }
        public WatermarkRowConfig(GeneralWatermarkRowProperty _row, Dictionary<string, object> meta)
        {
            try
            {
                InitializeComponent();
                row = _row;
                Meta = meta;
                vm = new WatermarkRowConfigVM(this);
                DataContext = vm;
            }
            catch(Exception ex)
            {
                Global.SendMsg(ex.Message);
            }
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
            InitData();
            InitExif();
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

        private double fontOpacity;
        public double FontOpacity
        {
            get => fontOpacity;
            set
            {
                fontOpacity = value;
                NotifyPropertyChanged(nameof(FontOpacity));
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

        private string edgeHeight;
        public string EdgeHeight
        {
            get => edgeHeight;
            set
            {
                edgeHeight = value;
                NotifyPropertyChanged(nameof(EdgeHeight));
            }
        }

        #endregion
        #region function

        private void InitExif()
        {
            var model = Global.InitConfig();
            var meta = new List<ExifInfo>(model.Exifs);
            foreach (var exif in meta)
            {
                if(window.Meta.TryGetValue(exif.Key, out object val))
                {
                    exif.Value = val is DateTime ? (string)Global.GetDateTimeFormat(window.row.DateFormat, val) : val.ToString();
                }
            }
            foreach(var i in window.Meta.Where(c=> !meta.Select(x=>x.Key).Contains(c.Key)))
            {
                var ei = new ExifInfo
                {
                    IsSelected = false,
                    Key = i.Key,
                    Name = i.Key,
                    Value = i.Value.ToString()
                };
                meta.Add(ei);
            }
            ExifInfoList = new ObservableCollection<ExifInfo>(meta);
            if (window.row.DataSource != null && window.row.DataSource.Exifs != null)
                Config = new ObservableCollection<ExifConfigInfo>(window.row.DataSource.Exifs);
            foreach (var cfg in Config.Where(c=>c.Key != "Customer"))
            {
                if(cfg == null) continue;
                var item = meta.FirstOrDefault(c => c.Key == cfg.Key);
                if(item != null && !string.IsNullOrEmpty(item.Value))
                {
                    cfg.Value = item.Value;
                }
            }
        }

        private void InitData()
        {
            FontColor = window.row.Color;
            FontZoom = window.row.FontXS;
            FontOpacity = window.row.FontOpacity;
            FontFamily = window.row.FontFamily;
            IsBold = window.row.IsBold;
            window.edgeComputeMode.SelectedIndex = window.row.EdgeDistanceType == EdgeDistanceType.Character ? 0 : 1;
            EdgeWidth = window.row.EdgeDistanceType == EdgeDistanceType.Character ? window.row.EdgeDistanceCharacterX : window.row.EdgeDistancePercent + "";
            EdgeHeight = window.row.EdgeDistanceType == EdgeDistanceType.Character ? window.row.EdgeDistanceCharacterY : window.row.EdgeDistancePercent + "";
            var fontList = Global.InitFontList();
            window.fontlist.ItemsSource = fontList;
            var fontIdx = fontList.IndexOf(window.row.FontFamily);
            window.fontlist.SelectedIndex = fontIdx;
            window.yearMon.Text = window.row.DateFormat[0];
            window.monDay.Text = window.row.DateFormat[1];
            window.hourMin.Text = window.row.DateFormat[2];
            window.minSec.Text = window.row.DateFormat[3];
            window.xAlign.SelectedIndex = (int)window.row.X / 2;
        }

        public void SaveConfig()
        {
            window.row.DataSource = new WatermarkDataSource
            {
                From = DataSourceFrom.Exif,
                Exifs = Config.ToList(),
            };
            foreach(var item in  Config)
            {
                window.Meta[item.Key] = item.Value;
            }
            window.row.Color = FontColor;
            window.row.DateFormat = new List<string> { window.yearMon.Text, window.monDay.Text, window.hourMin.Text, window.minSec.Text };
            window.row.FontXS = FontZoom;
            window.row.FontOpacity = (float)FontOpacity;
            window.row.FontFamily = FontFamily;
            window.row.IsBold = IsBold;
            window.row.X = (PositionBase)(window.xAlign.SelectedIndex * 2);
            if (window.edgeComputeMode.SelectedIndex == 0)
            {
                window.row.EdgeDistanceType = EdgeDistanceType.Character;
                window.row.EdgeDistanceCharacterX = EdgeWidth;
                window.row.EdgeDistanceCharacterY = EdgeHeight;
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
