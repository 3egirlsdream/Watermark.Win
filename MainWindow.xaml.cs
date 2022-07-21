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
        public static string logo = "";
        public static string binpath;
        public static string path;
        public static string sourceImgUrl;
        public static string lastUrl;
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
                binpath = AppDomain.CurrentDomain.BaseDirectory;
                path = binpath;//.Substring(0, binpath.IndexOf("bin"));
                xy.Text = "44°29′12\"E 33°23′46\"W";
                vm.Images = new ObservableCollection<ImageInstance>();

                InitLogoes();

                var delPath = binpath + "\\temp";
                DirectoryInfo directory = new DirectoryInfo(delPath);
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
                logo = path + "\\logo\\" + tag + ".png";
                if(MultiImages.Any() && !string.IsNullOrEmpty(logo))
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
                vm.Images.Add(new ImageInstance(MultiImages[0], MultiImages[0].Substring(MultiImages[0].LastIndexOf('\\') + 1)));
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

                InitExifInfo(url.Url);

                var Width = sourceImage.Width;
                var Height = sourceImage.Height;
                try
                {
                    var watermakPath = await CreateImage.CreatePic(Width, Height);
                    var c = Tuple.Create(Width, Height);
                    var dFileName = $"{DateTime.Now.ToString("yyyyMMddHHmmss")}{cc++}.jpg";
                    await CreateImage.AddWaterMarkImg(watermakPath, dFileName, $@"{logo}", datetime, deviceName.Text, sourceImage, c, false, mount.Text, xy.Text, 1, 1);
                    var output = binpath + "\\output\\" + dFileName;
                    
                    var previewUrl = binpath + "\\temp\\temp_" + dFileName;
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
            var p = binpath + "\\output";
            if (Directory.Exists(p))
            {
                System.Diagnostics.Process.Start(p);
            }

            //var dlg = new SaveFileDialog()
            //{
            //    Title = "另存为",
            //    DefaultExt = "jpg",
            //    Filter = "Jpg files (*.jpg)|*.jpg|All files|*.*",
            //};
            //if (dlg.ShowDialog() == true)
            //{

            //    Bitmap sourceImage = new Bitmap(lastUrl);
            //    sourceImage.Save(dlg.FileName);
            //}
        }

        private async void SetPreviewImg(bool f = true)
        {
            try
            {
                var url = MultiImages.Any() ? MultiImages[0] : sourceImgUrl;
                if (string.IsNullOrEmpty(url))
                    return;

                if (f)
                {
                    await Task.Delay(5000);
                }
                if ((DateTime.Now - LastDate).TotalSeconds < 5) return;
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
                var previewUrl = await CreateImage.CreateWatermark(watermarkPath, dFileName, $@"{logo}", datetime, deviceName.Text, sourceImage, c, true, mount.Text, xy.Text, 1, 1);
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
                logo = dialog.FileName;
            }
        }

        private void deviceName_TextChanged(object sender, TextChangedEventArgs e)
        {
            LastDate = DateTime.Now;
            if(MultiImages.Any() && !string.IsNullOrEmpty(logo) && !IsBreak)
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
                    sourceImgUrl = dialog.FileName;
                    sourceImg.Visibility = Visibility.Visible;
                    plus1.Visibility = Visibility.Collapsed;
                    listbox.Visibility = Visibility.Collapsed;
                }
                else
                {
                    foreach(var item in dialog.FileNames)
                    {
                        var i = new ImageInstance(item, item.Substring(item.LastIndexOf('\\') + 1));
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
            var path = binpath + "\\logo";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            DirectoryInfo directory = new DirectoryInfo(path);
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
                deviceName.Text = $"{rs["producer"]} {rs["model"]}";
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
