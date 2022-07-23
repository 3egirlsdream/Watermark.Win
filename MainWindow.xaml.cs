using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Image = System.Drawing.Image;

namespace JointWatermark
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public VM vm;
        private DateTime LastDate = DateTime.Now;
        public List<string> MultiImages = new List<string>();
        private bool IsBreak = false;
        public MainWindow()
        {
            try
            {
                InitializeComponent();
                MultiImages = new List<string>();
                vm = new VM();
                this.DataContext = vm;
                Global.BasePath = AppDomain.CurrentDomain.BaseDirectory;
                xy.Text = "44°29′12\"E 33°23′46\"W";
                vm.Images = new ObservableCollection<ImageInstance>();
                Global.Path_temp = Global.BasePath + $"{Global.SeparatorChar}temp";
                Global.Path_output = Global.BasePath + $"{Global.SeparatorChar}output";
                Global.Path_logo = Global.BasePath + $"{Global.SeparatorChar}logo";

                InitLogoes();

                DirectoryInfo directory = new DirectoryInfo(Global.Path_temp);
                if(directory.Exists)
                    directory.Delete(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
        


        private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is MaterialDesignThemes.Wpf.Card card && card.Tag is string tag)
            {
                Global.logo = Global.Path_logo + Global.SeparatorChar + tag + ".png";
                if(MultiImages.Any() && !string.IsNullOrEmpty(Global.logo))
                    SetPreviewImg(false);
            }
        }
        string datetime;
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            vm.Loading = true;
            int cc = 0;
            vm.OutputImages = new ObservableCollection<ImageInstance>();
            if (!MultiImages.Any())
            {
                return;
            }
            if (!vm.Images.Any())
            {
                vm.Images.Add(new ImageInstance(MultiImages[0], MultiImages[0].Substring(MultiImages[0].LastIndexOf(Global.SeparatorChar) + 1)));
            }
            foreach (var url in vm.Images)
            {

                Bitmap sourceImage = new Bitmap(url.Url);
                var img = Image.FromFile(url.Url);
                try
                {
                    var dt = img.GetPropertyItem(0x0132).Value;
                    var dateTimeStr = System.Text.Encoding.ASCII.GetString(dt).Trim('\0');
                    datetime = DateTime.ParseExact(dateTimeStr, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture).ToString("yyyy.MM.dd HH:mm:ss");
                }
                catch (Exception ex)
                {
                    datetime = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss");
                }

                if(vm.Images.Count > 1)
                    InitExifInfo(url.Url);

                var Width = sourceImage.Width;
                var Height = sourceImage.Height;
                try
                {
                    var watermakPath = await CreateImage.CreatePic(Width, Height);
                    var c = Tuple.Create(Width, Height);
                    var dFileName = $"{DateTime.Now.ToString("yyyyMMddHHmmss")}{cc++}.jpg";
                    await CreateImage.AddWaterMarkImg(watermakPath, dFileName, $@"{Global.logo}", datetime, deviceName.Text, sourceImage, c, false, mount.Text, xy.Text, 1, 1);
                    var output = Global.Path_output + Global.SeparatorChar + dFileName;
                    
                    var previewUrl = Global.Path_temp + $"{Global.SeparatorChar}temp_" + dFileName;
                    BitmapImage previwMap = new BitmapImage(new Uri(previewUrl, UriKind.Absolute));
                    preveiew.Source = previwMap;

                    var i = new ImageInstance(output, url.Name);
                    vm.OutputImages.Add(i);


                    listbox2.Visibility = Visibility.Visible;
                    createdImg.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    SnackbarThree.IsActive = true;
                    Task.Run(() =>
                    {
                        Thread.Sleep(2000);
                        Dispatcher.Invoke(() =>
                        {
                            SnackbarThree.IsActive = false;
                        });
                    });
                }

                if(vm.OutputImages.Count == 1)
                {
                    BitmapImage bitmap2 = new BitmapImage(new Uri(vm.OutputImages[0].Url, UriKind.Absolute));
                    createdImg.Source = bitmap2;
                    listbox2.Visibility = Visibility.Collapsed;
                    createdImg.Visibility = Visibility.Visible;
                }
            }



            vm.Loading = false;
        }

        private void downloadClick(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(Global.Path_output))
            {
                System.Diagnostics.Process.Start(Global.Path_output);
            }
        }

        private async void SetPreviewImg(bool f = true)
        {
            try
            {
                var url = MultiImages.Any() ? MultiImages[0] : Global.sourceImgUrl;
                if (string.IsNullOrEmpty(url))
                    return;

                if (f)
                {
                    await Task.Delay(5000);
                }
                if ((DateTime.Now - LastDate).TotalSeconds < 5 && f) return;
                Bitmap sourceImage = new Bitmap(url);
                var img = Image.FromFile(url);
                try
                {
                    var dt = img.GetPropertyItem(0x0132).Value;
                    var dateTimeStr = System.Text.Encoding.ASCII.GetString(dt).Trim('\0');
                    datetime = DateTime.ParseExact(dateTimeStr, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture).ToString("yyyy.MM.dd HH:mm:ss");
                }
                catch (Exception ex)
                {
                    datetime = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss");
                }

                var Width = sourceImage.Width;
                var Height = sourceImage.Height;

                var watermarkPath = await CreateImage.CreatePic(Width, Height);
                var c = Tuple.Create(Width, Height);
                var dFileName = $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.jpg";
                var previewUrl = await CreateImage.CreateWatermark(watermarkPath, dFileName, $@"{Global.logo}", datetime, deviceName.Text, sourceImage, c, true, mount.Text, xy.Text, 1, 1);
                BitmapImage previwMap = new BitmapImage(new Uri(previewUrl, UriKind.Absolute));
                preveiew.Source = previwMap;
            }
            catch (Exception ex)
            {
                SnackbarThree.IsActive = true;
                Task.Run(() =>
                {
                    Thread.Sleep(2000);
                    Dispatcher.Invoke(() =>
                    {
                        SnackbarThree.IsActive = false;
                    });
                });
            }
        }

        private void Card_MouseLeftButtonDown_1(object sender, MouseButtonEventArgs e)
        {

            // 实例化一个文件选择对象
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.DefaultExt = ".png";  // 设置默认类型
                                         // 设置可选格式
            dialog.Filter = @"图像文件(*.png)|*.png";
            // 打开选择框选择
            Nullable<bool> result = dialog.ShowDialog();
            if (result == true)
            {
                BitmapImage bitmap = new BitmapImage(new Uri(dialog.FileName, UriKind.Absolute));
                selfmade.Source = bitmap;// ; // 获取选择的文件名
                selfmade.Visibility = Visibility.Visible;
                plus.Visibility = Visibility.Collapsed;
                Global.logo = dialog.FileName;
            }
        }

        private void deviceName_TextChanged(object sender, TextChangedEventArgs e)
        {
            LastDate = DateTime.Now;
            if(MultiImages.Any() && !string.IsNullOrEmpty(Global.logo) && !IsBreak)
                SetPreviewImg();
        }

        private void SelectPictureClick(object sender, MouseButtonEventArgs e)
        {
            // 实例化一个文件选择对象
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.DefaultExt = ".png";  // 设置默认类型
            dialog.Multiselect = true;                             // 设置可选格式
            dialog.Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png
      |JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png";
            // 打开选择框选择
            Nullable<bool> result = dialog.ShowDialog();
            if (result == true)
            {
                vm.Images = new ObservableCollection<ImageInstance>();
                if (dialog.FileNames.Length == 1)
                {
                    BitmapImage bitmap = new BitmapImage(new Uri(dialog.FileName, UriKind.Absolute));
                    sourceImg.Source = bitmap;// ; // 获取选择的文件名
                    Global.sourceImgUrl = dialog.FileName;
                    sourceImg.Visibility = Visibility.Visible;
                    plus1.Visibility = Visibility.Collapsed;
                    listbox.Visibility = Visibility.Collapsed;
                }
                else
                {
                    foreach(var item in dialog.FileNames)
                    {
                        var i = new ImageInstance(item, item.Substring(item.LastIndexOf(Global.SeparatorChar) + 1));
                        vm.Images.Add(i);
                    }
                    sourceImg.Visibility = Visibility.Collapsed;
                    plus1.Visibility = Visibility.Collapsed;
                    listbox.Visibility = Visibility.Visible;
                }
                MultiImages = new List<string>(dialog.FileNames);
                if (MultiImages.Any())
                {
                    InitExifInfo(MultiImages[0]);
                }
            }
        }

        private void InitLogoes()
        {
            if (!Directory.Exists(Global.Path_logo))
            {
                Directory.CreateDirectory(Global.Path_logo);
            }
            DirectoryInfo directory = new DirectoryInfo(Global.Path_logo);
            var files = directory.GetFiles();
            logoes.Children.Clear();
            foreach(var file in files)
            {
                var card = new Card();
                card.Tag = file.Name.Split('.')[0];
                card.Margin = new Thickness(12, 0, 16, 0);
                card.Width = 60;
                card.Height = 60;

                var img = new System.Windows.Controls.Image();
                img.Width = 40;
                img.Height = 40;
                BitmapImage map = new BitmapImage(new Uri(file.FullName, UriKind.Absolute));
                img.Source = map;
                card.Content = img;
                card.Cursor = Cursors.Hand;
                ShadowAssist.SetShadowDepth(card, ShadowDepth.Depth1);
                card.MouseLeftButtonDown += Card_MouseLeftButtonDown;
                logoes.Children.Add(card);
            }
            addp.Children.Clear();
            logoes.Children.Add(add);
        }

        private void ListBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            SelectPictureClick(null, null);
        }

        private void InitExifInfo(string filePath)
        {
            try
            {
                IsBreak = true;
                mount.Text = "f/1.8 1/40 ISO 400";
                xy.Text = "44°29′12\"E 33°23′46\"W";

                var ex = new ExifInfo2();
                var rs = ex.GetImageInfo(filePath, Image.FromFile(filePath));

                if (!rs.ContainsKey("f") || !rs.ContainsKey("exposure")|| !rs.ContainsKey("ISO")|| !rs.ContainsKey("mm"))
                {
                    IsBreak = false;
                    return;
                }

                mount.Text = $"F/{rs["f"]} {rs["exposure"]} ISO{rs["ISO"]} {rs["mm"]}";
                if (showCor.IsChecked == true)
                {
                    deviceName.Text = $"{rs["producer"]} {rs["model"]}";
                }
                else
                {
                    deviceName.Text = $"{rs["model"]}";
                }

                if (rs.TryGetValue("mount", out string val) && !string.IsNullOrEmpty(val))
                {
                    xy.Text = val;
                }

                if (rs.TryGetValue("date", out string d) && !string.IsNullOrEmpty(d))
                {
                    datetime = d;
                }

                IsBreak = false;
            }
            catch(Exception ex)
            {
                IsBreak = false;
            }
        }

        private void CheckShowProducer(object sender, RoutedEventArgs e)
        {
            if (MultiImages.Any() && !string.IsNullOrEmpty(Global.logo))
            {
                InitExifInfo(MultiImages[0]);
                SetPreviewImg(false);
            }
        }
    }

    public class VM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void NotifyPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        public VM()
        {

        }

        private bool loading = false;
        public bool Loading
        {
            get => loading;
            set
            {
                loading = value;
                NotifyPropertyChanged(nameof(Loading));
            }
        }

        private string mount;
        public string Mount 
        {
            get => mount;
            set
            {
                mount = value;
                NotifyPropertyChanged(nameof(Mount));
            }
        }

        private string xy;
        public string XY
        {
            get => xy;
            set
            {
                xy = value;
                NotifyPropertyChanged(nameof(XY));
            }
        }

        private string date;
        public string Date
        {
            get => date;
            set
            {
                date = value;
                NotifyPropertyChanged(nameof(Date));
            }
        }

        private string deviceName;
        public string DeviceName
        {
            get => deviceName;
            set
            {
                deviceName = value;
                NotifyPropertyChanged(nameof(Mount));
            }
        }




        private ObservableCollection<ImageInstance> images;
        public ObservableCollection<ImageInstance> Images
        {
            get => images;
            set
            {
                images = value;
                NotifyPropertyChanged(nameof(Images));
            }
        }

        private ObservableCollection<ImageInstance> outputImages;
        public ObservableCollection<ImageInstance> OutputImages
        {
            get => outputImages;
            set
            {
                outputImages = value;
                NotifyPropertyChanged(nameof(OutputImages));
            }
        }
    }

    public class ImageInstance
    {
        public ImageInstance(string url, string name)
        {
            Url = url;
            Name = name;
        }
        public string Url { get; set; }
        public string Name { get; set; }
    }
}
