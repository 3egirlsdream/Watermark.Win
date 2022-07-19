using Microsoft.Win32;
using System;
using System.Collections.Generic;
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
        public static string logo = path+ "\\sony.png";
        public static string binpath;
        public static string path;
        public static string sourceImgUrl;
        public static string lastUrl;
        public VM vm;
        public MainWindow()
        {
            try
            {
                InitializeComponent();
                vm = new VM();
                this.DataContext = vm;
                binpath = AppDomain.CurrentDomain.BaseDirectory;
                path = binpath;//.Substring(0, binpath.IndexOf("bin"));
                logo = path + "\\sony.png";
                xy.Text = "44°29′12\"E 33°23′46\"W";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void SelectPictureClick(object sender, RoutedEventArgs e)
        {
            // 实例化一个文件选择对象
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.DefaultExt = ".png";  // 设置默认类型
                                         // 设置可选格式
            dialog.Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png
      |JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png";
            // 打开选择框选择
            Nullable<bool> result = dialog.ShowDialog();
            if (result == true)
            {
                BitmapImage bitmap = new BitmapImage(new Uri(dialog.FileName, UriKind.Absolute));
                sourceImg.Source = bitmap;// ; // 获取选择的文件名
                sourceImgUrl = dialog.FileName;
            }
        }


        private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is MaterialDesignThemes.Wpf.Card card && card.Tag is string tag)
            {
                logo = path + "\\" + tag + ".png";
                if(!string.IsNullOrEmpty(sourceImgUrl))
                    SetPreviewImg();
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            vm.Loading = true;
            Bitmap sourceImage = new Bitmap(sourceImgUrl);
            var img = Image.FromFile(sourceImgUrl);
            DateTime datetime;
            try
            {
                var dt = img.GetPropertyItem(0x0132).Value;
                var dateTimeStr = System.Text.Encoding.ASCII.GetString(dt).Trim('\0');
                datetime = DateTime.ParseExact(dateTimeStr, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                datetime = DateTime.Now;
            }

            var Width = sourceImage.Width;
            var Height = sourceImage.Height;
            try
            {
                await CreateImage.CreatePic(Width, Height);
                var c = Tuple.Create(Width, Height);
                var dFileName = $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.jpg";
                await CreateImage.AddWaterMarkImg($@"{binpath}\watermark.jpg", dFileName, $@"{logo}", datetime, deviceName.Text, sourceImage, c, false, mount.Text, xy.Text, 1, 1);
                lastUrl = binpath + "\\" + dFileName;
                BitmapImage bitmap2 = new BitmapImage(new Uri(lastUrl, UriKind.Absolute));
                createdImg.Source = bitmap2;
                var previewUrl = binpath + "\\temp_" + dFileName;
                BitmapImage previwMap = new BitmapImage(new Uri(previewUrl, UriKind.Absolute));
                preveiew.Source = previwMap;
            }
            catch(Exception ex)
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

            vm.Loading = false;
        }

        private void downloadClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog()
            {
                Title = "另存为",
                DefaultExt = "jpg",
                Filter = "Jpg files (*.jpg)|*.jpg|All files|*.*",
            };
            if (dlg.ShowDialog() == true)
            {

                Bitmap sourceImage = new Bitmap(lastUrl);
                sourceImage.Save(dlg.FileName);
            }
        }

        private async void SetPreviewImg()
        {
            try
            {
                Bitmap sourceImage = new Bitmap(sourceImgUrl);
                var img = Image.FromFile(sourceImgUrl);
                DateTime datetime;
                try
                {
                    var dt = img.GetPropertyItem(0x0132).Value;
                    var dateTimeStr = System.Text.Encoding.ASCII.GetString(dt).Trim('\0');
                    datetime = DateTime.ParseExact(dateTimeStr, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    datetime = DateTime.Now;
                }

                var Width = sourceImage.Width;
                var Height = sourceImage.Height;

                await CreateImage.CreatePic(Width, Height);
                var c = Tuple.Create(Width, Height);
                var dFileName = $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.jpg";
                await CreateImage.AddWaterMarkImg($@"{binpath}\watermark.jpg", dFileName, $@"{logo}", datetime, deviceName.Text, sourceImage, c, true, mount.Text, xy.Text, 1, 1);
                var previewUrl = binpath + "\\temp_" + dFileName;
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
            if(!string.IsNullOrEmpty(sourceImgUrl))
                SetPreviewImg();
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
    }
}
