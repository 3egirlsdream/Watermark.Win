using JointWatermark.Class;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JointWatermark.Views
{
    /// <summary>
    /// Loading.xaml 的交互逻辑
    /// </summary>
    public partial class Loading : Window
    {
        CancellationTokenSource tokenSource;
        LoadingVM vm;
        public Loading()
        {
            InitializeComponent();
            vm = new LoadingVM();
            DataContext = vm;
        }

        private bool mini = false;
        public bool Mini 
        { 
            get => mini; 
            set
            {
                mini = value;
                if(value)
                {
                    Height = 90;
                    Width = 300;
                }
            }
        }

        public Loading(Action<CancellationToken, Loading> action)
        {
            tokenSource = new CancellationTokenSource();
            InitializeComponent();

            vm = new LoadingVM();
            DataContext = vm;

            Task.Run(() =>
            {
                action(tokenSource.Token, this);
            }).ContinueWith(c =>
            {
                Thread.Sleep(200);
                Dispatcher.Invoke(() =>
                {
                    if (c.Exception != null)
                    {
                        Global.SendMsg(c.Exception.Message);
                    }
                });
                IClose();
            });
        }

        public void ISetPosition(int count, string msg = "")
        {
            vm.Text = msg;
            while (vm.Process < count)
            {
                vm.Process++;
                Thread.Sleep(10);
            }
        }

        public void IClose()
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                this.DialogResult = true;
            }));
        }


        private void Button_Click(object sender, RoutedEventArgs e)
        {
            tokenSource?.Cancel();
        }
    }

    public class LoadingVM : ValidationBase
    {
        public LoadingVM()
        {

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


        private int process;
        public int Process
        {
            get => process;
            set
            {
                process = value;
                NotifyPropertyChanged(nameof(Process));
            }
        }
    }
}
