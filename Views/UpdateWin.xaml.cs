using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace JointWatermark.Views
{
    /// <summary>
    /// UpdateWin.xaml 的交互逻辑
    /// </summary>
    public partial class UpdateWin : Window
    {
        public UpdateWin()
        {
            InitializeComponent();
        }

        private void MoveWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                WindowState = WindowState.Normal;
                DragMove();
            }
        }

        private void WindowMininizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var vm = new SettingVM(this);
            Visibility = Visibility.Hidden;
            var filePath = Global.BasePath + Global.SeparatorChar + "JointWatermark.Update.exe";
            var file = File.Exists(filePath);
            if (file)
            {
                File.Delete(filePath);
            }
            var r = vm.DownloadUpdateProgram(this);
            file = File.Exists(filePath);
            if (r == true && file)
            {
                System.Diagnostics.Process.Start(filePath);
            }
        }

        private void NextTime_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
