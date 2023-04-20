using JointWatermark.Class;
using JointWatermark.Views;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
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
    public partial class Setting : Window
    {
        SettingVM vm;
        public Setting()
        {
            InitializeComponent();
            vm = new SettingVM(this);
            DataContext = vm;
            CheckVersion(null, null);
            updatelog.Text = Global.GetUpdateLog();
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
                    Latest.Text = "你使用的是最新版本!";
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

    public class SettingVM : INotifyPropertyChanged
    {

        Setting window;
        public SettingVM(Setting check)
        {
            window = check;
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
                                Global.SendMsg("字体名称格式不正确！");
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
                        Global.SendMsg("字体名称格式不正确！");
                        return;
                    }
                    var f = window.normalText.Text;
                    var filename = f.Substring(f.LastIndexOf('\\') + 1);
                    var prex = Global.BasePath + Global.SeparatorChar + "fonts" + Global.SeparatorChar;
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
                    Global.SendMsg(ex.Message);
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

        private async void downloadFont(CloudFont first)
        {
            using (var wc = new WebClient())
            {
                try
                {
                    first.IsLoading = false;
                    var path = Global.BasePath + Global.SeparatorChar + "fonts" + Global.SeparatorChar;
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
                    Global.SendMsg(ex.Message);
                }
                finally
                {
                    first.IsLoading = true;
                    InitFontsList();
                }
            }

        }


    }
}
