using JointWatermark.Class;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace JointWatermark.Views
{
    /// <summary>
    /// Export.xaml 的交互逻辑
    /// </summary>
    public partial class Export : Window
    {
        ExportVM vm;
        List<string> savePath;
        public Export(ObservableCollection<GeneralWatermarkProperty> _images)
        {
            try
            {
                InitializeComponent();
                vm = new ExportVM(_images);
                savePath = Global.InitConfig().SavePath;
                InitSavePath(savePath);
                vm.SelectedItem = Global.Path_output;
                quality.Value = Global.Quality;
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
            Global.Quality = (int)(quality.Value);
            var data = Global.InitConfig();
            data.SavePath = savePath;
            Global.SaveConfig(JsonConvert.SerializeObject(data));
            this.DialogResult = true;
        }

        private void selecetExportFileClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();
            System.Windows.Forms.DialogResult result = dialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                var path = dialog.SelectedPath.Trim();
                if(!savePath.Any(c => c.Equals(path))) 
                {
                    vm.SelectedItem = path;
                    savePath.Add(path);
                    InitSavePath(savePath);
                }
            }
            
        }

        private void InitSavePath(List<string> path)
        {
            if(path.Count == 0)
            {
                path.Add(Global.Path_output);
            }
            vm.SavePathes = new ObservableCollection<string>(path);
        }

        private void exportPath_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if(sender is ComboBox cmb && !string.IsNullOrEmpty(cmb.Text))
            {
                Global.Path_output = cmb.Text;
            }
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

        private ObservableCollection<string> savePathes;
        public ObservableCollection<string> SavePathes
        {
            get => savePathes;
            set
            {
                savePathes = value;
                NotifyPropertyChanged(nameof(SavePathes));
            }
        }

        private string selectedItem;
        public string SelectedItem
        {
            get => selectedItem;
            set
            {
                selectedItem = value;
                NotifyPropertyChanged(nameof(SelectedItem));
                Global.Path_output = value;
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
