using JointWatermark.Class;
using System;
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
        CancellationTokenSource tokenSource;
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
