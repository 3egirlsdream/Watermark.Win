using JointWatermark.Class;
using Microsoft.Win32;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Image = SixLabors.ImageSharp.Image;

namespace JointWatermark.Views
{
    /// <summary>
    /// TemplatesMarket.xaml 的交互逻辑
    /// </summary>
    public partial class TemplatesMarket : Window
    {
        TemplatesMarketVM vm;
        List<string> savePath;
        public TemplatesMarket()
        {
            try
            {
                InitializeComponent();
                vm = new TemplatesMarketVM();
                var m = Global.InitConfig();
                savePath = m.SavePath;
                InitSavePath(savePath);
                vm.SelectedItem = Global.Path_output;
                DataContext = vm;
                InitTemplates();
                //vm.ImgSource = helper.ImageSharpToImageSource(img);
            }
            catch(Exception ex)
            {
                Global.SendMsg(ex.Message);
            }
        }

        public async void InitTemplates()
        {
            var model = Global.InitConfig();
            vm.ImgCollection.Clear();
            var helper = new ImagesHelper();
            var list = new List<GeneralWatermarkProperty>();
            list.Add(model.Templates.PhotoFrame);
            list.AddRange(model.Templates.CustomizationComponents.Select(c => c.Property));
            foreach (var template in list)
            {
                var example = Properties.Resources.DSC02852;
                using MemoryStream memoryStream = new MemoryStream();
                example.Save(memoryStream, ImageFormat.Jpeg);
                byte[] imageData = memoryStream.ToArray();

                var bt = Global.GetMeta(imageData);
                template.OriginalByte = imageData;
                template.Meta = bt;
                var file = await ImagesHelper.Current.Generation(template, true);
                var imgSource = helper.ImageSharpToImageSource(file);
                vm.ImgCollection.Add(new ImageSourceCollection(template.ID, imgSource));
            }

        }


        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var data = Global.InitConfig();
            data.SavePath = savePath;
            data.Quality = Global.Quality;
            Global.SaveConfig(JsonConvert.SerializeObject(data));
            this.DialogResult = true;
        }

        private void selecetTemplatesMarketFileClick(object sender, RoutedEventArgs e)
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

        private void TemplatesMarketPath_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if(sender is ComboBox cmb && !string.IsNullOrEmpty(cmb.Text))
            {
                Global.Path_output = cmb.Text;
            }
        }
    }

    public class TemplatesMarketVM : ValidationBase
    {

        public TemplatesMarketVM()
        {
            ImgCollection = new ObservableCollection<ImageSourceCollection>();
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


        private ObservableCollection<ImageSourceCollection> imageCollection;
        public ObservableCollection<ImageSourceCollection> ImgCollection
        {
            get => imageCollection;
            set
            {
                imageCollection = value;
                NotifyPropertyChanged(nameof(ImgCollection));
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


    public class ImageSourceCollection : ValidationBase
    {
        public ImageSourceCollection()
        {

        }

        public ImageSourceCollection(string id, ImageSource imageSource)
        {
            ID = id;
            ImgSource = imageSource;
        }

        public string ID { get; set; }

        private ImageSource imgSource;
        public ImageSource ImgSource
        {
            get => imgSource;
            set
            {
                imgSource = value;
                NotifyPropertyChanged(nameof(ImgSource));
            }
        }
    }
}
