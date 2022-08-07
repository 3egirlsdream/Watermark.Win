using JointWatermark.Class;
using JointWatermark.Views;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
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
                foreach (var item in vm.Images)
                {
                    item.Config.LogoName = name;
                }
                if (vm.SelectedImage != null)
                {
                    vm.RefreshSelectedImage(vm.SelectedImage);
                }
            }
        }

        private void downloadClick(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(Global.Path_output))
            {
                System.Diagnostics.Process.Start(Global.Path_output);
            }
        }


        private void deviceName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (vm.SelectedImage != null)
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
                createdImg.Source = null;
                var action = new Action<CancellationToken, Loading>((token, loading) =>
                {
                    for (int ii = 0; ii < dialog.FileNames.Length; ii++)
                    {
                        var item = dialog.FileNames[ii];
                        var percent = (ii+1)* 100.0 / dialog.FileNames.Length;
                        token.ThrowIfCancellationRequested();
                        loading.ISetPosition((int)percent, $"正在加载图片: {item.Substring(item.LastIndexOf('\\') + 1)}");
                        var i = new ImageProperties(item, item.Substring(item.LastIndexOf(Global.SeparatorChar) + 1));
                        var meta = Global.GetThumbnailPath(i.Path);
                        i.ThumbnailPath = meta.path;
                        i.Config.LeftPosition1 = meta.left1;
                        i.Config.LeftPosition2 = meta.left2;
                        i.Config.RightPosition1 = meta.right1;
                        i.Config.RightPosition2 = meta.right2;
                        i.Config.BackgroundColor = "#fff";
                        i.Path = item;

                        if (vm.IconList != null && vm.IconList.Any())
                        {
                            i.Config.LogoName = vm.IconList[0].Substring(vm.IconList[0].LastIndexOf(Global.SeparatorChar) + 1);
                        }
                        Dispatcher.Invoke(() =>
                        {
                            vm.Images.Add(i);
                        });
                    }
                });

                var ld = new Loading(action);
                ld.Owner = App.Current.MainWindow;
                ld.ShowDialog();
            }
        }

        public void InitLogoes()
        {
            try
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
            catch (Exception ex)
            {
                ((MainWindow)App.Current.MainWindow).SendMsg(ex.Message);
            }
        }

        private void ListBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            SelectPictureClick(null, null);
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
                    var p = Global.Path_output + Global.SeparatorChar + url.Path.Substring(url.Path.LastIndexOf(Global.SeparatorChar) + 1);
                    var bit = MyImages.Create(url).Result;
                    bit.SaveAsJpeg(p);
                    bit.Dispose();
                }
            });
            var ld = new Loading(action);
            ld.Owner = App.Current.MainWindow;
            var rst = ld.ShowDialog();
            if (rst == true)
            {
                var win = App.Current.MainWindow as MainWindow;
                win.ShowMsgBox("打开输出目录？");
                win.SetAction(() =>
                {
                    if (Directory.Exists(Global.Path_output))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo() { FileName = Global.Path_output, UseShellExecute = true };

                        System.Diagnostics.Process.Start(psi);
                    }
                });
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && vm != null && vm.Images != null)
            {
                foreach (var img in vm.Images)
                {
                    if (combo.SelectedIndex == 0)
                    {
                        img.Config.FontFamily = "Microsoft YaHei";
                    }
                    else
                    {
                        img.Config.FontFamily = "FZXiJinLJW";
                    }
                }
            }
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

        private string ouputImageUrl = "Resources/github.png";
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

        private BottomProcessInstance bottomProcess = new BottomProcessInstance(Visibility.Hidden, false);
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
            if (item == null) return;
            BottomProcess = new BottomProcessInstance(Visibility.Visible, true);
            try
            {
                var bit = await MyImages.Create(item, true);
                var bmp = MyImages.ImageSharpToImageSource(bit);
                mainPage.createdImg.Source = bmp;
                bit.Dispose();
            }
            catch (Exception ex)
            {
                ((MainWindow)App.Current.MainWindow).SendMsg(ex.Message);
            }
            BottomProcess = new BottomProcessInstance(Visibility.Hidden, false);
        } 

        public SimpleCommand CmdSaveGlobal => new SimpleCommand()
        {
            ExecuteDelegate = x =>
            {
                foreach (var item in Images)
                {
                    switch (x)
                    {
                        case "1": item.Config.LeftPosition1 = GlobalConfig.LeftPosition1;break;
                        case "2": item.Config.LeftPosition2 = GlobalConfig.LeftPosition2; break;
                        case "3": item.Config.RightPosition1 = GlobalConfig.RightPosition1; break;
                        case "4": item.Config.RightPosition2 = GlobalConfig.RightPosition2; break;
                        case "5": item.Config.BackgroundColor = GlobalConfig.BackgroundColor; break;
                        case "6": item.Config.BorderWidth = GlobalConfig.BorderWidth; break;
                        case "7": item.Config.Row1FontColor = GlobalConfig.Row1FontColor; break;
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
