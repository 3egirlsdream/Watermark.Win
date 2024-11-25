using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Watermark.Shared.Models;
using Watermark.Win.Models;

namespace Watermark.Win.Views
{
    /// <summary>
    /// CheckUpdate.xaml 的交互逻辑
    /// </summary>
    public partial class Setting : Window
    {
        SettingVM vm;
        public Setting()
        {
            InitializeComponent();
            vm = new SettingVM(this);
            DataContext = vm;
            CheckVersion(null, null);
        }

        private string? newPath;
        private async void CheckVersion(object sender, RoutedEventArgs e)
        {
            checkUpdateBtn.IsEnabled = false;
            var version = await Connections.HttpGetAsync<WMClientVersion>(APIHelper.HOST + "/api/CloudSync/GetVersion?Client=WatermarkV3", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.VERSION != null)
            {
                newPath = version.data.PATH;
                var v1 = new Version(this.version.Text);
                var v2 = new Version(version.data.VERSION);
                if (v2 > v1)
                {
                    Latest.Text = $"有新版本V{version.data.VERSION}点击下载";
                }
                else
                {
                    Latest.Text = "你使用的是最新版本!";
                    newVersion.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
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
                    catch (Exception)
                    {
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
            var version = await Connections.HttpGetAsync<WMClientVersion>(APIHelper.HOST + "/api/CloudSync/GetVersion?Client=WatermarkV3", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.VERSION != null)
            {
                newPath = version.data.PATH;
                var v1 = new Version(this.version.Text);
                var v2 = new Version(version.data.VERSION);
                if (v2 > v1)
                {
                    var filePath = AppDomain.CurrentDomain.BaseDirectory + "Watermark.Win.Update.exe";
                    var file = File.Exists(filePath);
                    if (file)
                    {
                        File.Delete(filePath);
                    }
                    var result = await ExcuteUpdateProgram();
                    file = File.Exists(filePath);
                    if (result == true && file)
                    {
                        System.Diagnostics.Process.Start(filePath);
                    }
                }
            }
        }

        private Task<bool> ExcuteUpdateProgram()
        {
            return vm.DownloadUpdateProgram();
        }

        private void ListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is ListBox box)
            {
                if (box.SelectedIndex == 0)
                {
                    t1?.Focus();
                }
                else if (box.SelectedIndex == 1)
                {
                    t2?.Focus();
                }
                else if (box.SelectedIndex == 2)
                {
                    t3?.Focus();
                }
                else if (box.SelectedIndex == 3)
                {
                    t4?.Focus();
                }
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(Global.AppPath.MarketFolder))
            {
                Directory.Delete(Global.AppPath.MarketFolder, true);
            }
            clearCache.Content = "清除完成";
        }

        private void clearDownloadCache_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(Global.AppPath.TemplatesFolder))
            {
                Directory.Delete(Global.AppPath.TemplatesFolder, true);
            }
            clearDownloadCache.Content = "清除完成";
        }

        private async void resetLogo_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(Global.AppPath.LogoesFolder))
            {
                Directory.Delete(Global.AppPath.LogoesFolder, true);
            }
            var api = new APIHelper();
            await api.DownloadLogoes();
            resetLogo.Content = "重置完成";
        }
    }

    public class SettingVM : INotifyPropertyChanged
    {

        Setting window;
        public SettingVM(Window check)
        {
            window = check as Setting;
            Text = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            InitFontsList();
            GlobalConfig.InitConfig().ContinueWith(x =>
            {
                ExifIsChecked = GlobalConfig.SECOND_EXIF;
                MaxThread = GlobalConfig.MAX_THREAD.ToString();
            });
            
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

        

        private bool exifIsChecked;
        public bool ExifIsChecked
        {
            get => exifIsChecked;
            set
            {
                exifIsChecked = value;
                GlobalConfig.SECOND_EXIF = value;
                NotifyPropertyChanged(nameof(ExifIsChecked));
            }
        }

        private string maxThread;
        public string MaxThread
        {
            get => maxThread;
            set
            {
                maxThread = value;
                if(int.TryParse(value, out int v))
                {
                    GlobalConfig.MAX_THREAD = v;
                }
                NotifyPropertyChanged(nameof(MaxThread));
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


        private ObservableCollection<WMCloudFont> fontsList;
        public ObservableCollection<WMCloudFont> FontsList
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
            var version = await Connections.HttpGetAsync<ObservableCollection<WMCloudFont>>(APIHelper.HOST + "/api/CloudSync/GetFontsList", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.Count > 0)
            {
                FontsList = version.data;
                var path = AppDomain.CurrentDomain.BaseDirectory + "fonts";
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

        public SimpleCommand CmdImportFont => new SimpleCommand()
        {
            ExecuteDelegate = x =>
            {
                // 实例化一个文件选择对象
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.DefaultExt = ".ttf";  // 设置默认类型
                dialog.Multiselect = false;                             // 设置可选格式
                dialog.Filter = @"字体文件(*.ttf,*.otf)|*ttf;*.otf";
                // 打开选择框选择
                Nullable<bool> result = dialog.ShowDialog();
                if (result == true)
                {
                    var f = dialog.FileName;

                    var file = new FileInfo(f);
                    if (file.Exists)
                    {
                        if ("normal" == x.ToString())
                        {
                            window.normalText.Text = f;
                        }
                        else
                        {
                            if (!f.Contains("-Bold"))
                            {
                                MessageBox.Show("字体名称格式不正确！");
                                return;
                            }
                            window.boldText.Text = f;
                        }

                    }

                }
            },
            CanExecuteDelegate = o => true
        };

        public SimpleCommand CmdSaveFont => new SimpleCommand()
        {
            ExecuteDelegate = x =>
            {
                try
                {
                    if (string.IsNullOrEmpty(window.normalText.Text) || string.IsNullOrEmpty(window.boldText.Text)) { return; }

                    if (!window.boldText.Text.Contains("-Bold"))
                    {
                        MessageBox.Show("字体名称格式不正确！");
                        return;
                    }
                    var f = window.normalText.Text;
                    var filename = f.Substring(f.LastIndexOf('\\') + 1);
                    var prex = AppDomain.CurrentDomain.BaseDirectory + "fonts" + Path.DirectorySeparatorChar;
                    var path = prex + filename;
                    var file = new FileInfo(f);
                    if (file.Exists)
                        file.CopyTo(path, true);
                    f = window.boldText.Text;
                    filename = f.Substring(f.LastIndexOf('\\') + 1);
                    path = prex + filename;
                    file = new FileInfo(f);
                    if (file.Exists)
                        file.CopyTo(path, true);

                    window.save.Content = "保存成功";
                    window.boldText.Text = "";
                    window.normalText.Text = "";


                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
                finally
                {
                    Task.Delay(2000).ContinueWith((t) =>
                    {
                        window.Dispatcher.Invoke(() =>
                        {
                            window.save.Content = "保存";
                        });
                    });
                }
            },
            CanExecuteDelegate = o => true
        };

        public SimpleCommand CmdDownloadFont => new()
        {
            ExecuteDelegate = x =>
            {
                if (x is string xx && !string.IsNullOrEmpty(xx))
                {
                    var first = FontsList.FirstOrDefault(c => c.ID == xx);
                    if (first != null)
                    {
                        downloadFont(first);
                    }
                }
            },
            CanExecuteDelegate = o => true
        };

        private async void downloadFont(WMCloudFont first)
        {
            using (var wc = new WebClient())
            {
                try
                {
                    first.IsLoading = false;
                    var path = AppDomain.CurrentDomain.BaseDirectory + "fonts" + Path.DirectorySeparatorChar;
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }
                    wc.DownloadProgressChanged += (ss, e) =>
                    {
                        first.Progress = e.ProgressPercentage;
                    };
                    var n1 = first.URL.Split(new string[] { "\\", "/" }, StringSplitOptions.RemoveEmptyEntries);
                    var n2 = first.URL_B.Split(new string[] { "\\", "/" }, StringSplitOptions.RemoveEmptyEntries);
                    await wc.DownloadFileTaskAsync(new Uri(first.URL), path + n1.Last());
                    await wc.DownloadFileTaskAsync(new Uri(first.URL_B), path + n2.Last());
                }
                catch (Exception ex)
                {
                }
                finally
                {
                    first.IsLoading = true;
                    InitFontsList();
                }
            }

        }

        public async Task<bool> DownloadUpdateProgram()
        {
            using (var wc = new WebClient())
            {
                var updatePath = "http://thankful.top:2038/api/public/dl/EMRyVNXX";
                var fileName = "Watermark.Win.Update.exe";
                string path = AppDomain.CurrentDomain.BaseDirectory + fileName;
                try
                {
                    wc.DownloadProgressChanged += (ss, e) =>
                    {
                        DownLoadProgress = e.ProgressPercentage;
                    };
                    await wc.DownloadFileTaskAsync(new Uri(updatePath), path);
                }
                catch (Exception ex)
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(ex.Message);
                    });
                    File.Delete(path);
                }

            }
            return true;
        }

    }
}
