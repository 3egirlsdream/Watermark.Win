using JointWatermark.Class;
using JointWatermark.Views;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Newtonsoft.Json;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Policy;
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
using WeakToys.Class;
using static System.Net.Mime.MediaTypeNames;

namespace JointWatermark
{
    /// <summary>
    /// MainPage.xaml 的交互逻辑
    /// </summary>
    public partial class MainPage : Page
    {
        public MainVM vm;
        public List<string> MultiImages = new();
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

                DirectoryInfo directory = new(Global.Path_temp);
                if (!directory.Exists)
                    directory.Create();

                InitFontList();
                ComputeUserCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void LBoxSort_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(LBoxSort);
                HitTestResult result = VisualTreeHelper.HitTest(LBoxSort, pos);
                if (result == null)
                {
                    return;
                }
                var listBoxItem = Utils.FindVisualParent<ListBoxItem>(result.VisualHit);
                if (listBoxItem == null || listBoxItem.Content != LBoxSort.SelectedItem)
                {
                    return;
                }
                DataObject dataObj = new DataObject(listBoxItem.Content as ImageProperties);
                DragDrop.DoDragDrop(LBoxSort, dataObj, DragDropEffects.Move);
            }
        }

        private void LBoxSort_OnDrop(object sender, DragEventArgs e)
        {
            var pos = e.GetPosition(LBoxSort);
            var result = VisualTreeHelper.HitTest(LBoxSort, pos);
            if (result == null)
            {
                return;
            }
            //查找元数据
            var sourcePerson = e.Data.GetData(typeof(ImageProperties)) as ImageProperties;
            if (sourcePerson == null)
            {
                return;
            }
            //查找目标数据
            var listBoxItem = Utils.FindVisualParent<ListBoxItem>(result.VisualHit);
            if (listBoxItem == null)
            {
                return;
            }
            var targetPerson = listBoxItem.Content as ImageProperties;
            if (ReferenceEquals(targetPerson, sourcePerson))
            {
                return;
            }
            vm.SplitImages.Remove(sourcePerson);
            vm.SplitImages.Insert(LBoxSort.Items.IndexOf(targetPerson), sourcePerson);
        }

        private void CloudIconCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is MaterialDesignThemes.Wpf.Card card && card.Tag is string tag)
            {
                foreach (var item in vm.Images)
                {
                    item.Config.LogoName = tag;
                    item.Config.IsCloudIcon = true;
                }
                if (vm.SelectedImage != null)
                {
                    vm.RefreshSelectedImage(vm.SelectedImage);
                }
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

        private void deviceName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (vm.SelectedImage != null)
            {
                vm.RefreshSelectedImage(vm.SelectedImage);
            }
        }

        public void SelectPictureClick(object sender, RoutedEventArgs e)
        {
            var tag = sender is Button button ? button.Tag : "";
            // 实例化一个文件选择对象
            Microsoft.Win32.OpenFileDialog dialog = new()
            {
                DefaultExt = ".png",  // 设置默认类型
                Multiselect = true,                             // 设置可选格式
                Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
            };
            // 打开选择框选择
            Nullable<bool> result = dialog.ShowDialog();
            if (result == true)
            {
                if ("split".Equals(tag))
                {
                    vm.SplitImages = new ObservableCollection<ImageProperties>();
                }
                else
                {
                    vm.Images = new ObservableCollection<ImageProperties>();
                }
                createdImg.Source = null;
                ImportImages(dialog.FileNames, tag.ToString());
            }
        }

        private void ImportImages(string[] filenames, string tag)
        {
            var action = new Action<CancellationToken, Loading>((token, loading) =>
            {
                for (int ii = 0; ii < filenames.Length; ii++)
                {
                    var item = filenames[ii];
                    var percent = (ii+1)* 100.0 / filenames.Length;
                    token.ThrowIfCancellationRequested();
                    loading.ISetPosition((int)percent, $"正在加载图片: {item.Substring(item.LastIndexOf('\\') + 1)}");
                    var i = ImagesHelper.Current.ReadImage(item, "");
                    if (vm.IconList != null && vm.IconList.Any())
                    {
                        var logoname = vm.IconList[0];
                        if (logoname.StartsWith("http"))
                        {
                            i.Config.LogoName = logoname;
                            i.Config.IsCloudIcon = true;
                        }
                        else
                        {
                            i.Config.LogoName = logoname.Substring(logoname.LastIndexOf(Global.SeparatorChar) + 1);
                            i.Config.IsCloudIcon = false;
                        }
                    }
                    Dispatcher.Invoke(() =>
                    {
                        if ("split".Equals(tag))
                        {
                            vm.SplitImages.Add(i);
                        }
                        else
                        {
                            vm.Images.Add(i);
                        }
                    });
                }
            });

            var ld = new Loading(action);
            ld.Owner = App.Current.MainWindow;
            ld.ShowDialog();
        }

        public void InitLogoes()
        {
            try
            {
                if (!Directory.Exists(Global.Path_logo))
                {
                    Directory.CreateDirectory(Global.Path_logo);
                }
                DirectoryInfo directory = new(Global.Path_logo);
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
                    BitmapImage map = new(new Uri(file.FullName, UriKind.Absolute));
                    img.Source = map;
                    card.Content = img;
                    card.Cursor = Cursors.Hand;
                    ElevationAssist.SetElevation(card, Elevation.Dp2);
                    card.MouseLeftButtonDown += Card_MouseLeftButtonDown;
                    logoes.Children.Add(card);
                    vm.IconList.Add(file.FullName);
                }
                var cloudIcons = Global.InitConfig().Icons;

                foreach(var icon in cloudIcons)
                {
                    try
                    {
                        var card = new Card();
                        card.Tag = icon;
                        card.Margin = new Thickness(12, 0, 16, 12);
                        card.Width = 60;
                        card.Height = 60;

                        var img = new System.Windows.Controls.Image();
                        img.Width = 40;
                        img.Height = 40;
                        BitmapImage map = new BitmapImage(new Uri(icon, UriKind.Absolute));
                        img.Source = map;
                        card.Content = img;
                        card.Cursor = Cursors.Hand;
                        ElevationAssist.SetElevation(card, Elevation.Dp2);
                        card.MouseLeftButtonDown += CloudIconCard_MouseLeftButtonDown;
                        card.ContextMenu = new ContextMenu();
                        var menuDel = new MenuItem();
                        menuDel.Icon = new PackIcon() { Kind = PackIconKind.Delete };
                        menuDel.Header = "删除";
                        menuDel.Click += (ss, es) => 
                        {
                            var model = Global.InitConfig();
                            model.Icons.Remove(icon);
                            var js = JsonConvert.SerializeObject(model);
                            Global.SaveConfig(js);
                            InitLogoes();
                        };
                        card.ContextMenu.Items.Add(menuDel);

                        logoes.Children.Add(card);
                        vm.IconList.Add(icon);
                    }
                    catch (Exception ex)
                    {
                        Global.SendMsg(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Global.SendMsg(ex.Message);
            }
        }

        private void ListBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            SelectPictureClick(new Button() { Tag = "watermark"}, null);
        }


        public void Export(IEnumerable<ImageProperties> images)
        {
            var action = new Action<CancellationToken, Loading>((token, loading) =>
            {
                foreach (var url in images)
                {
                    var percent = (images.ToList().IndexOf(url) + 1)  * 100.0 / images.Count();
                    loading.ISetPosition((int)percent, $"正在生成图片：{url.Path.Substring(url.Path.LastIndexOf(Global.SeparatorChar) + 1)}");
                    token.ThrowIfCancellationRequested();
                    var p = Global.Path_output + Global.SeparatorChar + url.Path.Substring(url.Path.LastIndexOf(Global.SeparatorChar) + 1);
                    var bit = ImagesHelper.Current.MergeWatermark(url).Result;
                    bit.Save(p);
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

        public void InitFontList()
        {
            var fonts = Global.FontResourrce.Select(c => c.Key).ToList();
            fonts.Insert(0, "微软雅黑");
            var path = Global.BasePath + Global.SeparatorChar + "fonts";
            if (Directory.Exists(path))
            {
                var files = new DirectoryInfo(path);
                foreach(var item in files.GetFiles())
                {
                    if (!item.Name.ToLower().Contains("bold"))
                    {
                        var ls  = item.Name.Split('/').Last().Split('.')[0];
                        fonts.Add(ls);
                    }
                }
            }
            fontlist.ItemsSource = fonts;
            fontlist2.ItemsSource = fonts;
        }


        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && vm != null && vm.Images != null)
            {
                foreach (var img in vm.Images)
                {
                    img.Config.FontFamily = combo.SelectedItem.ToString();
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            foreach(UIElement item in canvas.Children)
            {
                var left = Canvas.GetLeft(item);
            }
           
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            var label = new Label()
            {
                Content = "测试测试测试测试",
                FontSize = 100
            };
            label.MouseLeftButtonDown += Label_MouseLeftButtonDown;
            label.MouseMove +=Label_MouseMove;
            label.MouseLeftButtonUp += Label_MouseLeftButtonUp;
            Canvas.SetLeft(label, 200);
            canvas.Children.Add(label);
        }

        private void Label_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Label tmp = (Label)sender;
            ////MessageBox.Show(xx + " " + yy);
            tmp.ReleaseMouseCapture();
        }

        private void Label_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Label tmp = (Label)sender;
                double dx = e.GetPosition(null).X - pos.X + tmp.Margin.Left;
                double dy = e.GetPosition(null).Y - pos.Y + tmp.Margin.Top;
                Canvas.SetLeft(tmp, dx);
                Canvas.SetTop(tmp, dy);
                tmp.Margin = new Thickness(dx, dy, 0, 0);
                pos = e.GetPosition(null);
            }
        }

        System.Windows.Point pos = new();

        private void Label_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Label tmp = (Label)sender;
            pos = e.GetPosition(null);
            tmp.CaptureMouse();
            tmp.Cursor = Cursors.Hand;
        }
   

        private void RotateImageClick(object sender, RoutedEventArgs e)
        {
            if (vm.SelectedImage != null)
            {
                btnRotate.IsEnabled = false;
                vm.SelectedImage.Config.RotateCount++;
                vm.RefreshSelectedImage(vm.SelectedImage);
            }
        }

        private void Grid_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Link;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Grid_Drop(object sender, DragEventArgs e)
        {
            try
            {
                string[] fileName = (string[])e.Data.GetData(DataFormats.FileDrop);
                fileName = fileName.Where(c => c.EndsWith("g", StringComparison.OrdinalIgnoreCase)).ToArray();
                ImportImages(fileName, "watermark");
            }
            catch (Exception ex)
            {
                ((MainWindow)(App.Current.MainWindow)).SendMsg(ex.Message);
            }
        }


        private bool mouseDown;
        private System.Windows.Point mouseXY;
        private double min = 0.1, max = 3.0;//最小/最大放大倍数

        private void ContentControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var img = sender as ContentControl;
            if (img == null)
            {
                return;
            }
            img.CaptureMouse();
            mouseDown = true;
            mouseXY = e.GetPosition(img);
        }

        private void ContentControl_MouseMove(object sender, MouseEventArgs e)
        {
            var img = sender as ContentControl;
            if (img == null)
            {
                return;
            }
            if (mouseDown)
            {
                Domousemove(img, e);
            }
        }

        private void ContentControl_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var img = sender as ContentControl;
            if (img == null)
            {
                return;
            }
            var point = e.GetPosition(img);
            var group = createdImg.FindResource("TfGroup") as TransformGroup;
            var delta = e.Delta * 0.001;
            DowheelZoom(group, point, delta);
        }

        private void ContentControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var img = sender as ContentControl;
            if (img == null)
            {
                return;
            }
            img.ReleaseMouseCapture();
            mouseDown = false;
        }

        private void Domousemove(ContentControl img, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            var group = createdImg.FindResource("TfGroup") as TransformGroup;
            var transform = group.Children[1] as TranslateTransform;
            var position = e.GetPosition(img);
            transform.X -= mouseXY.X - position.X;
            transform.Y -= mouseXY.Y - position.Y;
            mouseXY = position;
        }

        private void ResetImagePositionClick(object sender, RoutedEventArgs e)
        {
            var group = createdImg.FindResource("TfGroup") as TransformGroup;
            var transform = group.Children[1] as TranslateTransform;
            transform.X = 0;
            transform.Y = 0;
            var transform2 = group.Children[0] as ScaleTransform;
            transform2.ScaleX = 1;
            transform2.ScaleY = 1;
        }

        private void CloseCharacterWatermarksConfig(object sender, RoutedEventArgs e)
        {
            vm.ShowCharacterConfig = Visibility.Collapsed;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (vm != null && vm.SelectedImage != null)
            {
                vm.RefreshSelectedImage(vm.SelectedImage);
            }
        }

        private void fontlist2_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && vm != null && vm.FocusCharacterWatermarks != null)
            {
                vm.FocusCharacterWatermarks.FontFamily = combo.SelectedItem.ToString();
                if (vm.SelectedImage != null)
                {
                    vm.RefreshSelectedImage(vm.SelectedImage);
                }
            }
        }

        private void AddCharacterWatermarksConfig(object sender, RoutedEventArgs e)
        {
            if (vm.SelectedImage == null) return;
            var item = new CharacterWatermarkProperty();
            vm.SelectedImage.Config.CharacterWatermarks.Add(item);
            vm.FocusCharacterWatermarks = item; 
        }

        private void DeleteCharacterWatermarksConfig(object sender, RoutedEventArgs e)
        {
            if (vm.SelectedImage == null) return;
            vm.SelectedImage.Config.CharacterWatermarks = new ObservableCollection<CharacterWatermarkProperty>(vm.SelectedImage.Config.CharacterWatermarks.Where(c => c.ID != vm.FocusCharacterWatermarks.ID));
            vm.SelectedImage.Config.CharacterWatermarks = null;
            vm.ShowCharacterConfig = Visibility.Collapsed;
        }

        private void colorpicker22_ColorChanged(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color> e)
        {
            if (vm != null && vm.SelectedImage != null)
            {
                vm.RefreshSelectedImage(vm.SelectedImage);
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (vm != null && vm.SelectedImage != null)
            {
                vm.RefreshSelectedImage(vm.SelectedImage);
            }
        }

        private void DowheelZoom(TransformGroup group, System.Windows.Point point, double delta)
        {
            var pointToContent = group.Inverse.Transform(point);
            var transform = group.Children[0] as ScaleTransform;
            if (transform.ScaleX + delta < min) return;
            if (transform.ScaleX + delta > max) return;
            transform.ScaleX += delta;
            transform.ScaleY += delta;
            var transform1 = group.Children[1] as TranslateTransform;
            transform1.X = -1 * ((pointToContent.X * transform.ScaleX) - point.X);
            transform1.Y = -1 * ((pointToContent.Y * transform.ScaleY) - point.Y);
        }

        private void ExportSplitImageClick(object sender, RoutedEventArgs e)
        {
            var horizon = splitDirection.IsChecked == true;
            if (vm.SplitImages == null ||  vm.SplitImages.Count == 0) return;
            foreach (var im in vm.SplitImages)
            {
                im.Config.BorderWidth = (int)split_border.Value;
            }
            var action = new Action<CancellationToken, Loading>((token, loading) =>
            {
                loading.ISetPosition(0, "已完成：0%");
                var p = Global.Path_output + Global.SeparatorChar + DateTime.Now.ToString("yyyyMMddHHmmss") + ".jpg";
                var bit = ImagesHelper.Current.SplitImages(vm.SplitImages, horizon, token, loading).Result;
                bit.Save(p);
                loading.ISetPosition(100, "已完成：100%");
                bit.Dispose();
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

        private async void ComputeUserCount()
        {
            try
            {
                var key = Global.Key();
                Debug.WriteLine(key);
                var c = await Connections.HttpGetAsync<bool>(Global.Http + "/api/CloudSync/ComputeUserCount?ID=" + key, Encoding.Default);
            }
            catch { }
        }
       
    }

    public class MainVM : ValidationBase
    {
        MainPage mainPage;
        public MainVM(Page page)
        {
            mainPage = page as MainPage;
            InitFontsList();
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


        private ObservableCollection<ImageProperties> splitImages;
        public ObservableCollection<ImageProperties> SplitImages
        {
            get => splitImages;
            set
            {
                splitImages = value;
                NotifyPropertyChanged(nameof(SplitImages));
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

        private ObservableCollection<CloudFont> fontsList;
        public ObservableCollection<CloudFont> FontsList
        {
            get=> fontsList;
            set
            {
                fontsList = value;
                NotifyPropertyChanged(nameof(FontsList));
            }
        }


        private ImageConfig globalConfig = new();
        public ImageConfig GlobalConfig
        {
            get => globalConfig;
            set
            {
                globalConfig = value;
                NotifyPropertyChanged(nameof(GlobalConfig));
            }
        }



        private ObservableCollection<string> iconList = new();
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

        private BottomProcessInstance bottomProcess = new(Visibility.Hidden, false);
        public BottomProcessInstance BottomProcess
        {
            get => bottomProcess;
            set
            {
                bottomProcess = value;
                NotifyPropertyChanged(nameof(BottomProcess));
            }
        }

        private CharacterWatermarkProperty focusCharacterWatermarks;
        /// <summary>
        /// 文字水印
        /// </summary>
        public CharacterWatermarkProperty FocusCharacterWatermarks
        {
            get => focusCharacterWatermarks;
            set
            {
                focusCharacterWatermarks = value;
                NotifyPropertyChanged(nameof(FocusCharacterWatermarks));
            }
        }

        private Visibility showCharacterConfig = Visibility.Collapsed;
        public Visibility ShowCharacterConfig
        {
            get=> showCharacterConfig;
            set
            {
                showCharacterConfig = value;
                NotifyPropertyChanged(nameof(ShowCharacterConfig));
            }
        }


        public SimpleCommand CmdClickItem => new()
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
            CanExecuteDelegate= o => true
        };


        public async void RefreshSelectedImage(ImageProperties item)
        {
            if (item == null) return;
            BottomProcess = new BottomProcessInstance(Visibility.Visible, true);
            try
            {
                if((DateTime.Now - ImagesHelper.Current.LastDate).TotalSeconds < 1.0)
                {
                    return;
                }
                var bit = await ImagesHelper.Current.MergeWatermarkPreview(item, true);
                var bmp = ImagesHelper.Current.ImageSharpToImageSource(bit);
                mainPage.createdImg.Source = bmp;
                bit.Dispose();
            }
            catch (Exception ex)
            {
                Global.SendMsg(ex.Message);
            }
            finally
            {
                await Task.Delay(1000);
                BottomProcess = new BottomProcessInstance(Visibility.Hidden, false);
                mainPage.btnRotate.IsEnabled = true;
            }
        } 

        public SimpleCommand CmdSaveGlobal => new()
        {
            ExecuteDelegate = x =>
            {
                foreach (var item in Images)
                {
                    if (!string.IsNullOrEmpty(GlobalConfig.LeftPosition1)) item.Config.LeftPosition1 = GlobalConfig.LeftPosition1;
                    if (!string.IsNullOrEmpty(GlobalConfig.LeftPosition2)) item.Config.LeftPosition2 = GlobalConfig.LeftPosition2;
                    if (!string.IsNullOrEmpty(GlobalConfig.RightPosition1)) item.Config.RightPosition1 = GlobalConfig.RightPosition1;
                    if (!string.IsNullOrEmpty(GlobalConfig.RightPosition2)) item.Config.RightPosition2 = GlobalConfig.RightPosition2;
                    if (!string.IsNullOrEmpty(GlobalConfig.BackgroundColor)) item.Config.BackgroundColor = GlobalConfig.BackgroundColor;
                    item.Config.BorderWidth = GlobalConfig.BorderWidth;
                    if (!string.IsNullOrEmpty(GlobalConfig.Row1FontColor)) item.Config.Row1FontColor = GlobalConfig.Row1FontColor;
                    if (GlobalConfig.FontXS != 1) item.Config.FontXS = GlobalConfig.FontXS;
                }

                RefreshSelectedImage(SelectedImage);
            },
            CanExecuteDelegate = o => true
        };

        public SimpleCommand CmdSetIcon => new()
        {
            ExecuteDelegate = x =>
            {
                if(SelectedImage != null && x is string c && !string.IsNullOrEmpty(c))
                {
                    if (c.StartsWith("http"))
                    {
                        SelectedImage.Config.IsCloudIcon = true;
                        SelectedImage.Config.LogoName = c;
                    }
                    else
                    {
                        SelectedImage.Config.LogoName = c.Substring(c.LastIndexOf(Global.SeparatorChar) + 1);
                        SelectedImage.Config.IsCloudIcon = false;
                    }
                    RefreshSelectedImage(SelectedImage);
                }
            },
            CanExecuteDelegate = o => true
        };


        public SimpleCommand CmdEditCharacterWatermark => new()
        {
            ExecuteDelegate = x =>
            {
                if (SelectedImage == null || SelectedImage.Config == null || SelectedImage.Config.CharacterWatermarks == null ||  SelectedImage.Config.CharacterWatermarks.Count == 0) return;
                FocusCharacterWatermarks = SelectedImage.Config.CharacterWatermarks.FirstOrDefault(c => c.ID == x.ToString());
                if(FocusCharacterWatermarks != null)
                {
                    ShowCharacterConfig = Visibility.Visible;
                }
                else
                {
                    ShowCharacterConfig = Visibility.Collapsed;
                }
            },
            CanExecuteDelegate = o => true
        };

        public SimpleCommand CmdDownloadFont => new()
        {
            ExecuteDelegate = x =>
            {
                if(x is string xx && !string.IsNullOrEmpty(xx))
                {
                    var first = FontsList.FirstOrDefault(c => c.ID == xx);
                    if(first != null)
                    {
                        downloadFont(first);
                    }
                }
            },
            CanExecuteDelegate = o => true
        };

        private async void downloadFont(CloudFont first)
        {
            using (var wc = new WebClient())
            {
                try
                {
                    first.IsLoading = false;
                    var path = Global.BasePath + Global.SeparatorChar + "fonts" + Global.SeparatorChar;
                    if(!Directory.Exists(path)) 
                    { 
                        Directory.CreateDirectory(path);
                    }
                    wc.DownloadProgressChanged += (ss, e) =>
                    {
                        first.Progress = e.ProgressPercentage;
                    };
                    var n1 = first.URL.Split(new string[] {"\\", "/" }, StringSplitOptions.RemoveEmptyEntries);
                    var n2 = first.URL_B.Split(new string[] { "\\", "/" }, StringSplitOptions.RemoveEmptyEntries);
                    await wc.DownloadFileTaskAsync(new Uri(first.URL), path + n1.Last());
                    await wc.DownloadFileTaskAsync(new Uri(first.URL_B), path + n2.Last());
                }
                catch (Exception ex)
                {
                    Global.SendMsg(ex.Message);
                }
                finally
                {
                    first.IsLoading = true;
                    mainPage.InitFontList();
                }
            }

        }

        private async void InitFontsList()
        {
            var version = await Connections.HttpGetAsync<ObservableCollection<CloudFont>>(Global.Http + "/api/CloudSync/GetFontsList", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.Count > 0)
            {
                FontsList = version.data;
                var path = Global.BasePath + Global.SeparatorChar + "fonts";
                if (Directory.Exists(path))
                {
                    var files = new DirectoryInfo(path);
                    foreach (var item in files.GetFiles())
                    {
                        var f = FontsList.FirstOrDefault(c => item.Name.Contains(c.NAME));
                        if (f != null)
                        {
                            f.IsLoading = false;
                        }
                    }
                }
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

    internal static class Utils
    {
        //根据子元素查找父元素
        public static T FindVisualParent<T>(DependencyObject obj) where T : class
        {
            while (obj != null)
            {
                if (obj is T)
                    return obj as T;

                obj = VisualTreeHelper.GetParent(obj);
            }
            return null;
        }
    }
}
