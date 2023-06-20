using JointWatermark.Class;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace JointWatermark.Views
{
    /// <summary>
    /// Export.xaml 的交互逻辑
    /// </summary>
    public partial class Export : Window
    {
        ExportVM vm;
        public Export(ObservableCollection<GeneralWatermarkProperty> _images)
        {
            try
            {
                InitializeComponent();
                vm = new ExportVM(_images);
                DataContext = vm;
            }
            catch(Exception ex)
            {
                Global.SendMsg(ex.Message);
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Global.Resolution = r1.IsChecked == true ? "default" : (r2.IsChecked == true ? "1080" : "4K");
            Global.ClearMeta = clearMeta.IsChecked == true;
            this.DialogResult = true;
        }
    }

    public class ExportVM : ValidationBase
    {

        public ExportVM(ObservableCollection<GeneralWatermarkProperty> _images)
        {
            this.Images = _images;
        }

        private ObservableCollection<GeneralWatermarkProperty> images;
        public ObservableCollection<GeneralWatermarkProperty> Images
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
                foreach (var image in Images)
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
            CanExecuteDelegate = o => true
        };
    }

}
