using JointWatermark.Class;
using System;
using System.Collections.Generic;
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
    /// Window1.xaml 的交互逻辑
    /// </summary>
    public partial class Window1 : Window
    {
        GeneralWatermarkProperty image;
        public Window1()
        {
            InitializeComponent();
            var c = DateTime.Now.ToString("yyyy.MM.dd HH.mm.ss");
            image = Global.Init();
        }

      
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var resultImage = ImagesHelper.Current.Generation(image);
            var source = ImagesHelper.Current.ImageSharpToImageSource(resultImage);
            img.Source = source;
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            var templateConfig = new TemplateConfig(image);
        }
    }
}
