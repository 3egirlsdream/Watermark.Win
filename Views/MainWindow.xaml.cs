using JointWatermark.Class;
using JointWatermark.Views;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace JointWatermark
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public VM vm;
        public MainWindow()
        {
            try
            {
                InitializeComponent();
                vm = new VM();
                this.DataContext = vm;
                Loaded += MainWindow_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var model = Global.InitConfig();
            if (model.ShowGuide)
            {
                var win = new UserGuide();
                win.Owner = this;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                win.ShowDialog();
            }
        }

        CheckUpdate checkUpdate = null;
        private void CheckUpdateClick(object sender, RoutedEventArgs e)
        {
            if(checkUpdate != null && checkUpdate.Activate())
            {
                checkUpdate.Visibility = Visibility.Visible;
            }
            else
            {
                checkUpdate = new CheckUpdate();
                checkUpdate.Owner = this;
                checkUpdate.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                checkUpdate.ShowInTaskbar = false;
                checkUpdate.ShowDialog();
            }
            
        }

        private void ImportIconClick(object sender, RoutedEventArgs e)
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
                foreach (var f in dialog.FileNames)
                {
                    var file = new FileInfo(f);
                    if (file.Exists)
                    {
                        var p = Global.Path_logo + Global.SeparatorChar + f.Substring(f.LastIndexOf('\\') + 1);
                        file.CopyTo(p, true);
                    }
                }
                main.InitLogoes();
            }
        }

        private void ImportImagesClick(object sender, RoutedEventArgs e)
        {
            main.SelectPictureClick(sender, null);
        }

        private void ExportImageClick(object sender, RoutedEventArgs e)
        {
            var director = new DirectoryInfo(Global.Path_output);
            if (!director.Exists)
            {
                director.Create();
            }
            Export export = new Export(main.vm.Images);
            export.Owner = this;
            export.ShowInTaskbar = false;
            export.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (export.ShowDialog() == true)
            {
                main.Export(main.vm.Images.Where(c=>c.IsChecked));
            }
        }

        private void ExportAllImageClick(object sender, RoutedEventArgs e)
        {

            var director = new DirectoryInfo(Global.Path_output);
            if (!director.Exists)
            {
                director.Create();
            }
            main.Export(main.vm.Images);
        }

        private void btn_Click(object sender, RoutedEventArgs e)
        {
            
        }

        Action action;

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            msg.Visibility = Visibility.Collapsed;
            action?.Invoke();
        }

        public void SetAction(Action _action)
        {
            action = _action;   
        }

        public void ShowMsgBox(string message)
        {
            msg_label.Content = message;
            DoubleAnimation yd5 = new DoubleAnimation(200, 0, new Duration(TimeSpan.FromSeconds(0.1)));//浮点动画定义了开始值和起始值
            msg.RenderTransform = new TranslateTransform();//在二维x-y坐标系统内平移(移动)对象
            //yd5.RepeatBehavior = RepeatBehavior.;//设置循环播放
            yd5.AutoReverse = false;//设置可以进行反转
            Storyboard.SetTarget(yd5, msg);//绑定动画为这个按钮执行的浮点动画
            Storyboard.SetTargetProperty(yd5, new PropertyPath("RenderTransform.X"));//依赖的属性
            Storyboard sb = new Storyboard();//首先实例化一个故事板
            sb.Children.Add(yd5);//向故事板中加入此浮点动画
            msg.Visibility = Visibility.Visible;
            sb.Begin();//播放此动画
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            msg.Visibility = Visibility.Collapsed;
        }

        private void SettingsClick(object sender, RoutedEventArgs e)
        {
            var win = new ConfigExif();
            win.Owner = this;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowDialog();
        }

        public void SendMsg(string msg)
        {
            SnackbarThree.MessageQueue.Enqueue(msg);
        }

        private void ExitClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ImportCloudIconClick(object sender, RoutedEventArgs e)
        {
            var win = new ImportCloudIcon();
            win.Owner = this;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowInTaskbar = false;
            if (win.ShowDialog() == true)
            {
                var model = Global.InitConfig();
                if (model != null)
                {
                    model.Icons.Add((string)win.Data);
                    var json = JsonConvert.SerializeObject(model);
                    Global.SaveConfig(json);
                }
                main.InitLogoes();
            }
        }

        private void OpenGuideClick(object sender, RoutedEventArgs e)
        {
            var win = new UserGuide();
            win.Owner = this;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowDialog();
        }

        private void OpenLink(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menu && menu.Tag is string tag && !string.IsNullOrEmpty(tag))
            {
                var psi = new System.Diagnostics.ProcessStartInfo() { FileName = tag, UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
        }

    }

    public class VM : ValidationBase
    {
        public VM()
        {

        }

    }

}
