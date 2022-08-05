using JointWatermark.Class;
using JointWatermark.Views;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
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
    /// MainPage.xaml 的交互逻辑
    /// </summary>
    public partial class MainPage : Page
    {
        public MainVM vm;
        private DateTime LastDate = DateTime.Now;
        public List<string> MultiImages = new List<string>();
        private bool IsBreak = false;
        public MainPage()
        {
            try
            {
                InitializeComponent();
                MultiImages = new List<string>();
                vm = new MainVM(this);
                this.DataContext = vm;
                Global.BasePath = AppDomain.CurrentDomain.BaseDirectory;
                xy.Text = "44°29′12\"E 33°23′46\"W";
                vm.Images = new ObservableCollection<ImageProperties>();
                Global.Path_temp = Global.BasePath + $"{Global.SeparatorChar}temp";
                Global.Path_output = Global.BasePath + $"{Global.SeparatorChar}output";
                Global.Path_logo = Global.BasePath + $"{Global.SeparatorChar}logo";

                InitLogoes();

                DirectoryInfo directory = new DirectoryInfo(Global.Path_temp);
                if (!directory.Exists)
                    directory.Create();
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
                var name = tag + ".png";
                foreach(var item in vm.Images)
                {
                    item.Config.LogoName = name;
                }
                if (vm.SelectedImage != null)
                {
                    vm.RefreshSelectedImage(vm.SelectedImage);
                }
            }
        }


        string datetime;
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            
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
                if ((DateTime.Now - LastDate).TotalSeconds < 2 && f) return;
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

                var emptyWatermark = await CreateImage.CreatePic(Width, Height);
                var c = Tuple.Create(Width, Height);
                var dFileName = $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.jpg";
                //var previewUrl = await CreateImage.CreateWatermark(emptyWatermark, url, c, mount.Text, xy.Text, 1, 1);
                //preveiew.Source = previewUrl;
                emptyWatermark.Dispose();
                sourceImage.Dispose();
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

        private void deviceName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if(vm.SelectedImage != null)
            {
                vm.RefreshSelectedImage(vm.SelectedImage);
            }
        }

        public void SelectPictureClick(object sender, MouseButtonEventArgs e)
        {
            // 实例化一个文件选择对象
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.DefaultExt = ".png";  // 设置默认类型
            dialog.Multiselect = true;                             // 设置可选格式
            dialog.Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png";
            // 打开选择框选择
            Nullable<bool> result = dialog.ShowDialog();
            if (result == true)
            {
                vm.Images = new ObservableCollection<ImageProperties>();
                var action = new Action<CancellationToken, Loading>((token, loading) =>
                {
                    for (int ii = 0; ii < dialog.FileNames.Length; ii++)
                    {
                        var item = dialog.FileNames[ii];
                        var percent = (ii+1)* 100.0 / dialog.FileNames.Length;
                        token.ThrowIfCancellationRequested();
                        loading.ISetPosition((int)percent, $"正在加载图片: {item.Substring(item.LastIndexOf('\\') + 1)}");
                        var rs = Global.InitExifInfo(item, true);
                        var i = new ImageProperties(item, item.Substring(item.LastIndexOf(Global.SeparatorChar) + 1));
                        i.Config.LeftPosition1 = rs.left1;
                        i.Config.LeftPosition2 = rs.left2;
                        i.Config.RightPosition1 = rs.right1;
                        i.Config.RightPosition2 = rs.right2;
                        i.Config.BackgroundColor = "#fff";
                        i.Path = item;
                        i.ThumbnailPath = GetThumbnailPath(i.Path);
                        Dispatcher.Invoke(() =>
                        {
                            vm.Images.Add(i);
                        });
                    }
                });

                var ld = new Loading(action);
                ld.Owner = App.Current.MainWindow;
                ld.ShowDialog();

                plus1.Visibility = Visibility.Collapsed;
            }
        }

        public void InitLogoes()
        {
            if (!Directory.Exists(Global.Path_logo))
            {
                Directory.CreateDirectory(Global.Path_logo);
            }
            DirectoryInfo directory = new DirectoryInfo(Global.Path_logo);
            var files = directory.GetFiles();
            logoes.Children.Clear();
            vm.IconList.Clear();
            foreach (var file in files)
            {
                var card = new Card();
                card.Tag = file.Name.Split('.')[0];
                card.Margin = new Thickness(12, 0, 16, 12);
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
                vm.IconList.Add(file.FullName);
            }
        }

        private void ListBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            SelectPictureClick(null, null);
        }

      
        private void CheckShowProducer(object sender, RoutedEventArgs e)
        {
        //    if (MultiImages.Any() && !string.IsNullOrEmpty(Global.logo))
        //    {
        //        InitExifInfo(MultiImages[0]);
        //        SetPreviewImg(false);
        //    }
        }

        private string GetThumbnailPath(string sourceImg)
        {
            using (var bitmap = new Bitmap(sourceImg))
            {

                if (bitmap.Width <= 1920 || bitmap.Height <= 1080)
                {
                    return sourceImg;
                }
                var xs = bitmap.Width / 1920M;

                var w = (int)(bitmap.Width / xs);
                var h = (int)(bitmap.Height / xs);
                using (var bp = new Bitmap(w, h))
                {
                    using (var g = Graphics.FromImage(bp))
                    {
                        g.DrawImage(bitmap, 0, 0, w, h);
                        var p = Global.Path_temp + Global.SeparatorChar + sourceImg.Substring(sourceImg.LastIndexOf('\\') + 1);
                        bp.Save(p, ImageFormat.Jpeg);
                        return p;
                    }

                }
            }

        }


        public void Export()
        {
            var action = new Action<CancellationToken, Loading>((token, loading) =>
            {
                foreach (var url in vm.Images)
                {
                    var percent = (vm.Images.IndexOf(url) + 1)  * 100.0 / vm.Images.Count;
                    loading.ISetPosition((int)percent, $"正在生成图片：{url.Path.Substring(url.Path.LastIndexOf(Global.SeparatorChar) + 1)}");
                    token.ThrowIfCancellationRequested();
                    var bit = vm.GenerateImage(url).Result;
                    var p = Global.Path_output + Global.SeparatorChar + url.Path.Substring(url.Path.LastIndexOf(Global.SeparatorChar) + 1);
                    Dispatcher.Invoke(() =>
                    {
                        bit.Save(p, ImageFormat.Jpeg);
                    });
                }
            });
            var ld = new Loading(action);
            ld.Owner = App.Current.MainWindow;
            ld.ShowDialog();

        }

    }

    public class MainVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void NotifyPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        MainPage mainPage;
        public MainVM(Page page)
        {
            mainPage = page as MainPage;
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


        private System.Windows.Media.Color color;
        public System.Windows.Media.Color Color
        {
            get => color;
            set
            {
                color = value;
                NotifyPropertyChanged(nameof(Color));
                Global.color =  System.Drawing.Color.FromArgb(value.R, value.G, value.B);
            }
        }

        private string ouputImageUrl = "../Resources/github.png";
        public string OuputImageUrl
        {
            get => ouputImageUrl;
            set
            {
                ouputImageUrl = value;
                NotifyPropertyChanged(nameof(OuputImageUrl));
            }
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


        private ImageProperties selectedImage;
        public ImageProperties SelectedImage
        {
            get => selectedImage;
            set
            {
                selectedImage = value;
                NotifyPropertyChanged(nameof(SelectedImage));
            }
        }


        private ImageConfig globalConfig = new ImageConfig();
        public ImageConfig GlobalConfig
        {
            get => globalConfig;
            set
            {
                globalConfig = value;
                NotifyPropertyChanged(nameof(GlobalConfig));
            }
        }



        private ObservableCollection<string> iconList = new ObservableCollection<string>();
        public ObservableCollection<string> IconList
        {
            get => iconList;
            set
            {
                iconList = value;
                NotifyPropertyChanged(nameof(IconList));
            }
        }

        private ObservableCollection<ImageProperties> outputImages;
        public ObservableCollection<ImageProperties> OutputImages
        {
            get => outputImages;
            set
            {
                outputImages = value;
                NotifyPropertyChanged(nameof(OutputImages));
            }
        }

        private BottomProcessInstance bottomProcess = new BottomProcessInstance(Visibility.Collapsed, false);
        public BottomProcessInstance BottomProcess
        {
            get => bottomProcess;
            set
            {
                bottomProcess = value;
                NotifyPropertyChanged(nameof(BottomProcess));
            }
        }


        public SimpleCommand CmdClickItem => new SimpleCommand()
        {
            ExecuteDelegate = x =>
            {
                var item = Images.FirstOrDefault(c => c.ID.Equals(x));
                if(item != null)
                {
                    SelectedImage = item;
                    RefreshSelectedImage(item);
                }
            },
            CanExecuteDelegate=o => true
        };


        public async void RefreshSelectedImage(ImageProperties item)
        {
            BottomProcess = new BottomProcessInstance(Visibility.Visible, true);
            var bitmap = await GenerateImage(item, true);
            var bitmapImage = new BitmapImage();
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Jpeg);
                ms.Seek(0, SeekOrigin.Begin);
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = ms;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                bitmap.Dispose();
            }

            mainPage.createdImg.Source = bitmapImage;
            BottomProcess = new BottomProcessInstance(Visibility.Collapsed, false);
        } 

        public SimpleCommand CmdSaveGlobal => new SimpleCommand()
        {
            ExecuteDelegate = x =>
            {
                foreach (var item in Images)
                {
                    switch (x)
                    {
                        case "0": item.Config.ShowBrandName = GlobalConfig.ShowBrandName; break;
                        case "1": item.Config.LeftPosition1 = GlobalConfig.LeftPosition1;break;
                        case "2": item.Config.LeftPosition2 = GlobalConfig.LeftPosition2; break;
                        case "3": item.Config.RightPosition1 = GlobalConfig.RightPosition1; break;
                        case "4": item.Config.RightPosition2 = GlobalConfig.RightPosition2; break;
                        case "5": item.Config.BackgroundColor = GlobalConfig.BackgroundColor; break;
                        default:break;
                    }
                }

                RefreshSelectedImage(SelectedImage);
            },
            CanExecuteDelegate = o => true
        };

        public SimpleCommand CmdSetIcon => new SimpleCommand()
        {
            ExecuteDelegate = x =>
            {
                if(SelectedImage != null && x is string c && !string.IsNullOrEmpty(c))
                {
                    SelectedImage.Config.LogoName = c.Substring(c.LastIndexOf(Global.SeparatorChar) + 1);
                    RefreshSelectedImage(SelectedImage);
                }
            },
            CanExecuteDelegate=o => true
        };


        public async Task<Bitmap> GenerateImage(ImageProperties url, bool isPreview = false)
        {
            Bitmap bitmap_Source;
            if (!isPreview)
            {
                bitmap_Source = new Bitmap(url.Path);
            }
            else
            {
                bitmap_Source = new Bitmap(url.ThumbnailPath);
            }

            var Width = bitmap_Source.Width;
            var Height = bitmap_Source.Height;
            try
            {
                var bitmap_Watermak = await CreateImage.CreatePic(Width, Height);
                var c = Tuple.Create(Width, Height);

                var watermark = await CreateImage.CreateWatermark(bitmap_Watermak, url.Config, c, 1, 1);
                var bitmap_output = await CreateImage.AddWaterMarkImg(watermark, bitmap_Source, c);
                bitmap_Watermak.Dispose();
                bitmap_Source.Dispose();
                return bitmap_output;
            }
            catch (Exception ex)
            {
                throw ex;
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

    public class BottomProcessInstance : ValidationBase
    {
        public BottomProcessInstance(Visibility v, bool s)
        {
            Visibility = v;
            IsLoading = s;
        }


        private Visibility visibility;
        public Visibility Visibility
        {
            get => visibility;
            set
            {
                visibility = value;
                NotifyPropertyChanged(nameof(Visibility));
            }
        }


        private bool isLoading;
        public bool IsLoading
        {
            get => isLoading;
            set
            {
                isLoading = value;
                NotifyPropertyChanged(nameof(IsLoading));
            }
        }
    }
}
