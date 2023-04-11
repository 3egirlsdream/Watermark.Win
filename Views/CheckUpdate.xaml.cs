using JointWatermark.Class;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WeakToys.Class;

namespace JointWatermark
{
    /// <summary>
    /// CheckUpdate.xaml 的交互逻辑
    /// </summary>
    public partial class CheckUpdate : Window
    {
        CheckUpdateVM vm;
        public CheckUpdate()
        {
            InitializeComponent();
            vm = new CheckUpdateVM();
            DataContext = vm;
            CheckVersion(null, null);
        }

        private string? newPath;
        private async void CheckVersion(object sender, RoutedEventArgs e)
        {
            checkUpdateBtn.IsEnabled = false;
            var version = await Connections.HttpGetAsync<CLIENT_VERSION>(Global.Http + "/api/CloudSync/GetVersion?Client=Watermark", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.VERSION != null)
            {
                newPath = version.data.PATH;
                var v1 = new Version(this.version.Text);
                var v2 = new Version(version.data.VERSION);
                if (v2 > v1)
                {
                    check.Badge = new PackIcon() { Kind = PackIconKind.Update };
                    newVersion.Visibility = Visibility.Visible;
                    Latest.Text = $"有新版本V{version.data.VERSION}点击下载";
                }
                else
                {
                    check.Badge = "";
                    Latest.Text = "已是最新";
                    newVersion.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                check.Badge = "";
                newVersion.Visibility = Visibility.Collapsed;
            }
            checkUpdateBtn.IsEnabled = true;
        }

        private async void newVersionClick(object sender, RoutedEventArgs e)
        {
            using (var wc = new WebClient())
            {
                var fileName = newPath.Substring(newPath.LastIndexOf('/') + 1);
                if (!fileName.ToLower().Contains(".zip"))
                {
                    fileName += ".zip";
                }
                SaveFileDialog pSaveFileDialog = new SaveFileDialog
                {
                    Title = "保存为:",
                    FileName = fileName,
                    RestoreDirectory = true,
                    Filter = "所有文件(*.*)|*.*"
                };//同打开文件，也可指定任意类型的文件
                if (pSaveFileDialog.ShowDialog() == true)
                {
                    string path = pSaveFileDialog.FileName;
                    vm.DownloadLoading = true;
                    newVersion.IsEnabled = false;
                    try
                    {
                        wc.DownloadProgressChanged += (ss, e) =>
                        {
                            vm.DownLoadProgress = e.ProgressPercentage;
                        };
                        checkUpdateBtn.IsEnabled = false;
                        await wc.DownloadFileTaskAsync(new Uri(newPath), path);
                    }
                    catch (Exception ex)
                    {
                        Global.SendMsg(ex.Message);
                    }
                    finally
                    {
                        vm.DownloadLoading = false;
                        newVersion.IsEnabled = true;
                        checkUpdateBtn.IsEnabled = true;
                    }
                }
            }
        }

        private void MoveWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                WindowState = WindowState.Normal;
                DragMove();
            }
        }

        private void TabItem_GotFocus(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void WindowMininizeClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var version = await Connections.HttpGetAsync<CLIENT_VERSION>(Global.Http + "/api/CloudSync/GetVersion?Client=Watermark", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.VERSION != null)
            {
                newPath = version.data.PATH;
                var v1 = new Version(this.version.Text);
                var v2 = new Version(version.data.VERSION);
                if (v2 > v1)
                {
                    System.Diagnostics.Process.Start(Global.BasePath + Global.SeparatorChar + "JointWatermark.Update.exe");
                }
            }
        }

        private void ListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if(sender is ListBox box)
            {
                if (box.SelectedIndex == 0)
                {
                    t1?.Focus();
                }
                else if (box.SelectedIndex == 1)
                {
                    t2?.Focus();
                }
            }
        }

    }

    public class CheckUpdateVM : INotifyPropertyChanged
    {
       
        public CheckUpdateVM()
        {
            Text = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            InitFontsList();
        }


        private string text = "";
        public string Text
        {
            get => text;
            set
            {
                text = value;
                NotifyPropertyChanged(nameof(Text));
            }
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


        private bool downloadLoading = false;
        public bool DownloadLoading
        {
            get => downloadLoading;
            set
            {
                downloadLoading = value;
                NotifyPropertyChanged(nameof(DownloadLoading));
            }
        }

        private int downLoadProgress = 0;
        public int DownLoadProgress
        {
            get => downLoadProgress;
            set
            {
                downLoadProgress = value;
                NotifyPropertyChanged(nameof(DownLoadProgress));
            }
        }


        private ObservableCollection<CloudFont> fontsList;
        public ObservableCollection<CloudFont> FontsList
        {
            get => fontsList;
            set
            {
                fontsList = value;
                NotifyPropertyChanged(nameof(FontsList));
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged = null;
        protected virtual void NotifyPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
}
