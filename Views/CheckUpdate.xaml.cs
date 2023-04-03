using JointWatermark.Views;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
                    //newVersion.Content = $"有新版本V{version.data.VERSION}点击下载";
                }
                else
                {
                    check.Badge = "";
                    checkUpdateBtn.Content = "已是最新";
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
            this.Visibility = Visibility.Hidden;
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
                    var filePath = Global.BasePath + Global.SeparatorChar + "JointWatermark.Update.exe";
                    var file = File.Exists(filePath);
                    if (file)
                    {
                        File.Delete(filePath);
                    }
                    var result = ExcuteUpdateProgram();
                    file = File.Exists(filePath);
                    if (result == true && file)
                    {
                        System.Diagnostics.Process.Start(filePath);
                    }
                }
            }
        }

        private bool? ExcuteUpdateProgram()
        {
            using (var wc = new WebClient())
            {
                var updatePath = "http://thankful.top:2038/api/public/dl/wzXaErGP";
                var fileName = "JointWatermark.Update.exe";
                string path = Global.BasePath + Global.SeparatorChar + fileName;
                var action = new Action<CancellationToken, Loading>((token, loading) =>
                {
                    try
                    {
                        wc.DownloadProgressChanged += (ss, e) =>
                        {
                            loading.ISetPosition(e.ProgressPercentage, $"更新程序已下载{e.ProgressPercentage}%");
                        };
                        wc.DownloadFileTaskAsync(new Uri(updatePath), path).Wait();
                    }
                    catch(Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            Global.SendMsg(ex.Message);
                        });
                        File.Delete(path);
                    }
                });
                var ld = new Loading(action);
                ld.Owner = this;
                ld.Mini = true;
                ld.ShowInTaskbar = false;
                ld.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                return ld.ShowDialog();
            }
        }
    }

    public class CheckUpdateVM : INotifyPropertyChanged
    {
       
        public CheckUpdateVM()
        {
            Text = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
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
        


        public event PropertyChangedEventHandler? PropertyChanged = null;
        protected virtual void NotifyPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }


    public class CLIENT_VERSION
    {
        public string? ID { get; set; }
        public DateTime DATETIME { get; set; }
        public string? CLIENT { get; set; }
        public string? VERSION { get; set; }
        public string? PATH { get; set; }
    }
}
