using JointWatermark.Class;
using JointWatermark.Enums;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace JointWatermark.Views
{
    /// <summary>
    /// WatermarkLogoConfig.xaml 的交互逻辑
    /// </summary>
    public partial class WatermarkLogoConfig : Window
    {
        WatermarkLogoConfigVM vm;
        public GeneralWatermarkRowProperty row { get; set; }
        public WatermarkLogoConfig(GeneralWatermarkRowProperty _row)
        {
            try
            {
                InitializeComponent();
                row = _row;
                vm = new WatermarkLogoConfigVM(this);
                DataContext = vm;
            }
            catch(Exception ex)
            {
                Global.SendMsg(ex.Message);
            }
        }

       

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            vm.SaveConfig();
            this.DialogResult = true;
        }
    }

    public class WatermarkLogoConfigVM : ValidationBase
    {
        WatermarkLogoConfig? window;
        public WatermarkLogoConfigVM(Window _window)
        {
            window = _window as WatermarkLogoConfig;
            InitData();
        }

        #region properties

     
        private int imagePercentOfRange;
        public int ImagePercentOfRange
        {
            get => imagePercentOfRange;
            set
            {
                imagePercentOfRange = value;
                NotifyPropertyChanged(nameof(ImagePercentOfRange));
            }
        }
       
        #endregion
        #region function

       

        private void InitData()
        {
            ImagePercentOfRange = window.row.ImagePercentOfRange;
        }

        public void SaveConfig()
        {
            window.row.ImagePercentOfRange = ImagePercentOfRange;
        }


        #endregion

    }
}
