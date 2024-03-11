using System.IO;
using System.Windows;
using System.Windows.Input;
using Watermark.Win.Models;
using Watermark.Win.Views;

namespace Watermark.Win.Views
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
            var setting = new Setting();
            setting.Owner = Application.Current.MainWindow;
            setting.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            setting.ShowInTaskbar = false;
            setting.ShowDialog();
            this.DialogResult = true;
        }

        private void NextTime_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
