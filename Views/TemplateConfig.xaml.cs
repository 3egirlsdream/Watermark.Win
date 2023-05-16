using JointWatermark.Class;
using JointWatermark.Enums;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace JointWatermark.Views
{
    /// <summary>
    /// TemplateConfig.xaml 的交互逻辑
    /// </summary>
    public partial class TemplateConfig : Page
    {
        TemplateConfigVM vm;
        public MainPage parent { get; private set; }
        public GeneralWatermarkProperty property { get; private set; }
        public TemplateConfig(GeneralWatermarkProperty _property, MainPage mainPage)
        {
            InitializeComponent();
            property = _property;
            parent = mainPage;
            BitmapImage map = new BitmapImage(new Uri(_property.PhotoPath, UriKind.Absolute));
            yaofan.Source = map;
            try
            {
                vm = new TemplateConfigVM(this);
                this.DataContext = vm;
            }
            catch(Exception ex)
            {
                Global.SendMsg(ex.Message);
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var model = Global.InitConfig();
            if (model.Templates == null) model.Templates = new WatermarkTemplates();
            model.Templates.PhotoFrame = property;
            model.Templates.PhotoFrame.Shadow.Enabled = vm.EnabledShadow;
            Global.SaveConfig(JsonConvert.SerializeObject(model));
        }

        private void togle1_Click(object sender, RoutedEventArgs e)
        {
            var dialog = ColorPickerWPF.ColorPickerWindow.ShowDialog(out System.Windows.Media.Color color,  ColorPickerWPF.Code.ColorPickerDialogOptions.SimpleView);
            if(dialog == true)
            {
                System.Drawing.Color _c = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
                vm.BackgroundColor = ColorTranslator.ToHtml(_c);
                vm.CmdRefresh.Execute(null);
            }
        }
    }

    public class TemplateConfigVM : ValidationBase
    {
        TemplateConfig window;
        public TemplateConfigVM(TemplateConfig window)
        {
            this.window = window;
            var ids = new List<string>();
            WaterItems1 = new ObservableCollection<GeneralWatermarkRowProperty>(window.property.Properties.Where(c => c.ContentType == ContentType.Text));
            foreach (GeneralWatermarkRowProperty property in window.property.Properties.Where(c => c.ContentType != ContentType.Text))
            {
                WaterItems1.Add(property);
            }
            ConnectionItems1 = new ObservableCollection<ConnectionMode>(window.property.ConnectionModes);
            for (var i = 0; i < ConnectionItems1.Count; i++)
            {
                var g = window.property.Properties.Where(c => ConnectionItems1[i].Ids.Contains(c.ID)).Select(c => c.Name);
                ConnectionItems1[i].Name = "组合:" + string.Join("\r\n", g);
            }
            InitData();
        }


        private ObservableCollection<GeneralWatermarkRowProperty> waterItems1;
        public ObservableCollection<GeneralWatermarkRowProperty> WaterItems1
        {
            get => waterItems1;
            set
            {
                waterItems1 = value;
                NotifyPropertyChanged(nameof(WaterItems1));
            }
        }

        private ObservableCollection<ConnectionMode> connectionItems1
;
        public ObservableCollection<ConnectionMode> ConnectionItems1

        {
            get => connectionItems1;
            set
            {
                connectionItems1 = value;
                NotifyPropertyChanged(nameof(ConnectionItems1));
            }
        }

        private int borderWidthOfTop;
        public int BorderWidthOfTop
        {
            get => borderWidthOfTop;
            set
            {
                borderWidthOfTop = value;
                NotifyPropertyChanged(nameof(BorderWidthOfTop));
            }
        }

        private int borderWidthOfBottom;
        public int BorderWidthOfBottom
        {
            get => borderWidthOfBottom;
            set
            {
                borderWidthOfBottom = value;
                NotifyPropertyChanged(nameof(BorderWidthOfBottom));
                ComputePercent(borderWidth);
            }
        }

        private int borderWidthOfLeft;
        public int BorderWidthOfLeft
        {
            get => borderWidthOfLeft;
            set
            {
                borderWidthOfLeft = value;
                NotifyPropertyChanged(nameof(BorderWidthOfLeft));
            }
        }

        private int borderWidthOfRight;
        public int BorderWidthOfRight
        {
            get => borderWidthOfRight;
            set
            {
                borderWidthOfRight = value;
                NotifyPropertyChanged(nameof(BorderWidthOfRight));
            }
        }

        private int borderWidth;
        public int BorderWidth
        {
            get => borderWidth;
            set
            {
                borderWidth = value;
                NotifyPropertyChanged(nameof(BorderWidth));
                window.property.StartPosition = new SixLabors.ImageSharp.Point(value, value);
                ComputePercent(value);
            }
        }

        private void ComputePercent(int value)
        {
            BorderWidthOfTop = value;
            BorderWidthOfLeft = value;
            BorderWidthOfRight = value;
            window.property.PecentOfHeight = 100 - value - BorderWidthOfBottom;
            window.property.PecentOfWidth = 100 - value * 2;
        }

        private int percentOfHeight;
        public int PercentOfHeight
        {
            get => percentOfHeight;
            set
            {
                percentOfHeight = value;
                NotifyPropertyChanged(nameof(PercentOfHeight));
            }
        }

        private int percentOfWidth;
        public int PercentOfWidth
        {
            get => percentOfWidth;
            set
            {
                percentOfWidth = value;
                NotifyPropertyChanged(nameof(PercentOfWidth));
            }
        }

        private bool enabledShadow;
        public bool EnabledShadow
        {
            get => enabledShadow;
            set
            {
                enabledShadow = value;
                NotifyPropertyChanged(nameof(EnabledShadow));
                if(window.property.Shadow != null)
                    window.property.Shadow.Enabled = enabledShadow;
                window.parent.vm.RefreshSelectedImage();
            }
        }

        private bool whiteToTransparent;
        public bool WhiteToTransparent
        {
            get => whiteToTransparent;
            set
            {
                whiteToTransparent = value;
                NotifyPropertyChanged(nameof(WhiteToTransparent));
                window.property.WhiteToTransparent = value;
                window.parent.vm.RefreshSelectedImage();
            }
        }
        

        private string backgroundColor = "#FFFFFF";
        public string BackgroundColor
        {
            get => backgroundColor;
            set
            {
                //if (backgroundColor.Length >= 9)
                //{
                //    backgroundColor = value.Remove(1, 2);
                //}
                //else
                //{
                    backgroundColor = value;
                //}
                NotifyPropertyChanged(nameof(BackgroundColor));
                window.property.BackgroundColor = backgroundColor;
            }
        }

        #region function
        public void InitData()
        {
            BorderWidthOfTop = window.property.StartPosition.Y;
            BorderWidthOfLeft = window.property.StartPosition.X;
            PercentOfHeight = window.property.PecentOfHeight;
            PercentOfWidth = window.property.PecentOfWidth;
            borderWidth = Math.Min(BorderWidthOfTop, BorderWidthOfLeft);
            BorderWidthOfBottom = 100 - PercentOfHeight - BorderWidthOfTop;
            BorderWidthOfRight = /*100 - PercentOfWidth -*/ BorderWidthOfLeft;
            //BorderWidth = Math.Min(BorderWidthOfTop, BorderWidthOfLeft);
            EnabledShadow = window.property.Shadow.Enabled;
            WhiteToTransparent = window.property.WhiteToTransparent;
            window.logoPage.GetPath = new Action<Photo>((photo) =>
            {
                window.property.Properties.ForEach(c =>
                {
                    if (c.ImagePath != null && c.ImagePath.IsLogo)
                    {
                        c.ImagePath.Path = photo.Path;
                        c.ImagePath.IsCloud = photo.IsCloud;
                    }
                });
                window.parent.vm.RefreshSelectedImage();
            });
            BackgroundColor = window.property.BackgroundColor;
        }

        #endregion

        public SimpleCommand CmdOpenConfig => new SimpleCommand()
        {
            ExecuteDelegate=x =>
            {
                var row = WaterItems1.FirstOrDefault(c => c.ID.Equals(x));
                if (row == null) return;
                Window page;
                if (row.ContentType == ContentType.Image)
                {
                    page = new WatermarkLogoConfig(row);
                }
                else
                {
                    page = new WatermarkRowConfig(row, window.property, window.parent.vm.Images);
                }
                page.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                if(page.ShowDialog() == true)
                {
                    window.parent.vm.RefreshSelectedImage();
                }
            },
            CanExecuteDelegate = o => true
        };

        public SimpleCommand CmdOpenGroupConfig => new SimpleCommand()
        {
            ExecuteDelegate = x =>
            {
                var row = ConnectionItems1.FirstOrDefault(c => c.ID.Equals(x));
                if (row == null) return;
                var page = new WatermarkConnectionConfig(row);
                page.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                if (page.ShowDialog() == true)
                {
                    window.parent.vm.RefreshSelectedImage();
                }
            },
            CanExecuteDelegate=o => true
        };

        public SimpleCommand CmdRefresh => new SimpleCommand()
        {
            ExecuteDelegate = x =>
            {
                window.parent.vm.RefreshSelectedImage();
            },
            CanExecuteDelegate = o => true
        };

        public SimpleCommand CmdSaveAs => new SimpleCommand()
        {
            ExecuteDelegate = x =>
            {
                var win = new MessageBoxL(false, "另存为", "", "另存为模板");
                win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                if(win.ShowDialog() == true)
                {
                    var component = new CustomizationComponent();
                    component.Name = win.Data;
                    component.Property = JsonConvert.DeserializeObject<GeneralWatermarkProperty>(JsonConvert.SerializeObject(window.property));
                    component.Property.Meta = null;
                    var model = Global.InitConfig();
                    if (model.Templates == null) model.Templates = new WatermarkTemplates();
                    if (model.Templates.CustomizationComponents == null) model.Templates.CustomizationComponents = new List<CustomizationComponent>() { component };
                    else model.Templates.CustomizationComponents.Add(component);
                    Global.SaveConfig(JsonConvert.SerializeObject(model));
                    window.parent.InitTemplates();
                }
            },
            CanExecuteDelegate = o => true
        };
    }
}
