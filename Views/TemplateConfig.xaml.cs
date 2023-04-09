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
    public partial class TemplateConfig : Page
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
            model.Templates.PhotoFrame.Shadow.Enabled = vm.EnabledShadow;
            Global.SaveConfig(JsonConvert.SerializeObject(model));
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
            ConnectionItems1 = new ObservableCollection<ConnectionMode>(window.property.ConnectionModes);
            for(var i = 0; i < ConnectionItems1.Count; i++)
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
                NotifyPropertyChanged(nameof(ConnectionItems1
));
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
                page.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                page.ShowDialog();
            },
            CanExecuteDelegate=o => true
        };

        public SimpleCommand CmdOpenGroupConfig => new SimpleCommand()
        {
            ExecuteDelegate=x =>
            {
                var row = ConnectionItems1.FirstOrDefault(c => c.ID.Equals(x));
                if (row == null) return;
                var page = new WatermarkRowConfig(row);
                page.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                page.ShowDialog();
            },
            CanExecuteDelegate=o => true
        };
    }
}
