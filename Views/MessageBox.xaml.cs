using JointWatermark.Class;
using System;
using System.Windows;
using System.Windows.Input;

namespace JointWatermark
{
    /// <summary>
    /// CheckUpdate.xaml 的交互逻辑
    /// </summary>
    public partial class MessageBoxL : Window
    {
       MessageBoxVM vm;
        public Func<string, bool> func;
        public dynamic Data { get; set; }
        public MessageBoxL()
        {
            InitializeComponent();
            vm = new MessageBoxVM(this);
            DataContext = vm;
        }

        public MessageBoxL(bool readOnly, string _title, string text, string tooltip = "")
        {
            InitializeComponent();
            vm = new MessageBoxVM(this);
            DataContext = vm;
            vm.Text = text;
            vm.IsReadOnly = readOnly;
            title.Text = _title;
            this.tooltip.ToolTip = tooltip;
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
    }

    public class MessageBoxVM : ValidationBase
    {

        MessageBoxL window;
        public MessageBoxVM(Window _win)
        {
            window = _win as MessageBoxL;
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

        private bool isReadOnly = false;
        public bool IsReadOnly
        {
            get=> isReadOnly;
            set
            {
                isReadOnly= value;
                NotifyPropertyChanged(nameof(IsReadOnly));
            }
        }

        
        public SimpleCommand CmdOK => new SimpleCommand()
        {
            ExecuteDelegate = o =>
            {
                bool passed = true;
                if (window.func != null)
                {
                    passed = window.func(Text);
                }
                if (passed)
                {
                    window.Data = Text;
                    window.DialogResult = true;
                }
            },
            CanExecuteDelegate = x => !string.IsNullOrEmpty(Text)
        };


    }

}
