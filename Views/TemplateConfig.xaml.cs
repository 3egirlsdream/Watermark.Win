using JointWatermark.Class;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public partial class TemplateConfig : Window
    {
        TemplateConfigVM vm;
        public GeneralWatermarkProperty property { get; private set; }
        public TemplateConfig(GeneralWatermarkProperty _property)
        {
            InitializeComponent();
            property = _property;
            vm = new TemplateConfigVM(this);
            this.DataContext = vm;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var model = Global.InitConfig();
            if (model.Templates == null) model.Templates = new WatermarkTemplates();
            model.Templates.PhotoFrame = property;
            Global.SaveConfig(JsonConvert.SerializeObject(model));
        }
    }

    public class TemplateConfigVM : ValidationBase
    {
        TemplateConfig window;
        public TemplateConfigVM(TemplateConfig window)
        {
            this.window = window;
            WaterItems1 = new ObservableCollection<GeneralWatermarkRowProperty>(window.property.Properties.Where(c => c.ContentType == ContentType.Text));
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
            }
        }

        #region function
        public void InitData()
        {
            BorderWidthOfTop = window.property.StartPosition.Y;
            BorderWidthOfLeft = window.property.StartPosition.X;
            PercentOfHeight = window.property.PecentOfHeight;
            PercentOfWidth = window.property.PecentOfWidth;
            BorderWidthOfBottom = 100 - PercentOfHeight - BorderWidthOfTop;
            BorderWidthOfRight = 100 - PercentOfWidth - BorderWidthOfLeft;
            EnabledShadow = window.property.Shadow.Enabled;
        }
        #endregion

        public SimpleCommand CmdOpenConfig => new SimpleCommand()
        {
            ExecuteDelegate=x =>
            {
                var row = WaterItems1.FirstOrDefault(c => c.ID.Equals(x));
                if (row == null) return;
                var page = new WatermarkRowConfig(row);
                page.Owner = this.window;
                page.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                page.ShowDialog();
            },
            CanExecuteDelegate=o => true
        };
    }
}
