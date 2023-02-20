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
    /// Export.xaml 的交互逻辑
    /// </summary>
    public partial class Export : Window
    {
        ExportVM vm;
        public Export(ObservableCollection<ImageProperties> _images)
        {
            InitializeComponent();
            vm = new ExportVM(_images);
            DataContext = vm;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Global.Resolution = r1.IsChecked == true ? "default" : (r2.IsChecked == true ? "1080" : "4K");
            this.DialogResult = true;
        }
    }

    public class ExportVM : ValidationBase
    {

        public ExportVM(ObservableCollection<ImageProperties> _images)
        {
            this.Images = _images;
        }

        private ObservableCollection<ImageProperties> images;
        public ObservableCollection<ImageProperties> Images
        {
            get => images;
            set
            {
                images = value;
                NotifyPropertyChanged(nameof(Images));
            }
        }

        private bool isCheckedAll;
        public bool IsCheckedAll
        {
            get => isCheckedAll;
            set
            {
                isCheckedAll = value;
                NotifyPropertyChanged(nameof(IsCheckedAll));
                foreach(var image in Images) 
                {
                    image.IsChecked = value;
                }
            }
        }



        

        public SimpleCommand CmdClickItem => new SimpleCommand()
        {
            ExecuteDelegate = x =>
            {
                var item = Images.FirstOrDefault(c => c.ID.Equals(x));
                if (item != null)
                {
                    item.IsChecked = !item.IsChecked;
                }
            },
            CanExecuteDelegate= o => true
        };
    }
}
