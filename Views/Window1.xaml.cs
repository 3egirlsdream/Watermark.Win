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
        public Window1()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var image = new GeneralWatermarkProperty();
            image.PhotoPath = "C:\\Users\\kingdee\\Desktop\\watermark\\a.png";
            image.StartPosition = new SixLabors.ImageSharp.Point(10, 10);
            image.PecentOfHeight = 80;
            image.PecentOfWidth = 80;
            image.EnableFixedPercent = true;
            ImagesHelper.Current.Generation(image);
        }
    }
}
