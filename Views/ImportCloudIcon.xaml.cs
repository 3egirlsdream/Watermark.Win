using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MaterialDesignThemes.Wpf;
using System.Net;
using Microsoft.Win32;
using WeakToys.Class;
using System.ComponentModel;
using JointWatermark.Class;

namespace JointWatermark
{
    /// <summary>
    /// CheckUpdate.xaml 的交互逻辑
    /// </summary>
    public partial class ImportCloudIcon : Window
    {
        ImportCloudIconVM vm;
        public dynamic Data { get; set; }
        public ImportCloudIcon()
        {
            InitializeComponent();
            vm = new ImportCloudIconVM(this);
            DataContext = vm;
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

    public class ImportCloudIconVM : ValidationBase
    {

        ImportCloudIcon window;
        public ImportCloudIconVM(Window _win)
        {
            window = _win as ImportCloudIcon;
        }


        private string url = "";
        public string Url
        {
            get => url;
            set
            {
                url = value;
                NotifyPropertyChanged(nameof(Url));
            }
        }

        private bool CheckUrl(string target)
        {
            try
            {
                Uri uri = new Uri(target); //.net4.5报错位置
                Console.WriteLine(uri.AbsoluteUri);
            }
            catch (Exception ex)
            {
                Global.SendMsg(ex.Message);
                return false;
            }

            return true;
        }

        public SimpleCommand CmdOK => new SimpleCommand()
        {
            ExecuteDelegate = o =>
            {
                if (CheckUrl(Url))
                {
                    window.Data = Url;
                    window.DialogResult = true;
                }
            },
            CanExecuteDelegate = x => !string.IsNullOrEmpty(Url)
        };


    }

}
