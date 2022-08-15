using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MaterialDesignThemes.Wpf;
using System.Net;
using Microsoft.Win32;
using WeakToys.Class;
using System.ComponentModel;

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
                    newVersion.Content = $"有新版本V{version.data.VERSION}点击下载";
                }
                else
                {
                    check.Badge = "";
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
                    wc.DownloadProgressChanged += (ss, e) =>
                    {
                        vm.DownLoadProgress = e.ProgressPercentage;
                    };
                    checkUpdateBtn.IsEnabled = false;
                    await wc.DownloadFileTaskAsync(new Uri(newPath), path);
                    vm.DownloadLoading = false;
                    newVersion.IsEnabled = true;
                    checkUpdateBtn.IsEnabled = true;
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
