using JointWatermark.Class;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JointWatermark.Views
{
    /// <summary>
    /// Error.xaml 的交互逻辑
    /// </summary>
    public partial class Error : Window
    {
        ErrorVM vm;
        public Error(string error)
        {
            InitializeComponent();
            vm = new ErrorVM(error);
            DataContext = vm;
        }


        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Fix_Click(object sender, RoutedEventArgs e)
        {
            var action = new Action<CancellationToken, Loading>((token, loading) =>
            {
                var folder = new DirectoryInfo(Global.BasePath);
                if (folder.Exists)
                {
                    loading.ISetPosition(0);
                    foreach (var file in folder.GetFiles())
                    {
                        token.ThrowIfCancellationRequested();
                        if (file.Exists && file.Extension != "exe" && (file.Name == "ExifConfig.json" || file.Extension == "dll"))
                        {
                            try
                            {
                                file.Delete();
                            }
                            catch { }
                        }
                    }
                    loading.ISetPosition(100, "修复完成");
                }
            });

            var dialog = new MessageBoxL(true, "警告", "请确保程序在独立的文件夹中！否则可能会影响其他文件，继续修复？");
            if (dialog.ShowDialog() == true)
            {
                var ld = new Loading(action);
                ld.Owner = this;
                ld.ShowInTaskbar = false;
                ld.Mini = true;
                ld.ShowDialog();
            }
        }
    }

    public class ErrorVM : ValidationBase
    {
        public ErrorVM(string error)
        {
            Text = error;
        }

        private string text;
        public string Text
        {
            get => text;
            set
            {
                text = value;
                NotifyPropertyChanged(nameof(Text));
            }
        }
    }
}
