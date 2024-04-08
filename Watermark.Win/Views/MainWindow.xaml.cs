using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MudBlazor.Services;
using Watermark.Win.Models;
using Watermark.Win.Views;
using System.IO;
using Watermark.Shared.Models;

namespace Watermark.Win
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            try
            {
                IocHelper.GetIoc().AddSingleton<IWMWatermarkHelper, WatermarkHelper>();
                Resources.SetIoc();
                InitializeComponent();
                Loaded+=MainWindow_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CheckUpdate();
        }

        protected override void OnClosed(EventArgs e)
        {
            var path = Global.AppPath.ThumbnailFolder;
            if(Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        public void CheckUpdate()
        {
            var day = DateTime.Now.DayOfYear;
            if (day % 3 == 0)
            {
#pragma warning disable CS8602 // 解引用可能出现空引用。
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
#pragma warning restore CS8602 // 解引用可能出现空引用。
                var action = new Action<string, string>((t, m) =>
                {
                    var win = new UpdateWin();
                    win.updatelog.Text = m;
                    win.msg.Content = t;
                    win.ShowInTaskbar = false;
                    win.Owner = this;
                    win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    win.ShowDialog();
                });

                CheckUpdate(v, action);
            }
        }

        public async void CheckUpdate(string nowv, Action<string, string> action)
        {
            var version = await Connections.HttpGetAsync<WMClientVersion>(APIHelper.HOST + "/api/CloudSync/GetVersion?Client=WatermarkV3", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.VERSION != null)
            {
                var v1 = new Version(nowv);
                var v2 = new Version(version.data.VERSION);
                if (v2 > v1)
                    action.Invoke($"有新版本V{version.data.VERSION}可以下载", version.data.MEMO);
            }
        }
    }
}