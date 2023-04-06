using JointWatermark.Class;
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
        public GeneralWatermarkProperty property { get;private set; }
        public TemplateConfig(GeneralWatermarkProperty _property)
        {
            InitializeComponent();
            property = _property;
            vm = new TemplateConfigVM(this);
            this.DataContext = vm;
        }
    }

    public class TemplateConfigVM: ValidationBase
    {
        TemplateConfig window;
        public TemplateConfigVM(TemplateConfig window)
        {
            this.window = window;
            WaterItems1 = new ObservableCollection<GeneralWatermarkRowProperty>(window.property.Properties.Where(c=>c.ContentType == ContentType.Text));
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
    }
}
