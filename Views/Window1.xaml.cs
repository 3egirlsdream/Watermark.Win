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
            image.PhotoPath = "C:\\Users\\Jiang\\Desktop\\屏幕截图 2022-05-29 193136.png";
            image.StartPosition = new SixLabors.ImageSharp.Point(10, 10);
            image.PecentOfHeight = 80;
            image.PecentOfWidth = 80;
            image.EnableFixedPercent = true;
            image.Properties = new List<GeneralWatermarkRowProperty>
            {
                new GeneralWatermarkRowProperty()
                {
                    X = PositionBase.Left,
                    Y = PositionBase.Bottom,
                    EdgeDistanceType = EdgeDistanceType.Character,
                    EdgeDistanceCharacterX = "ABCD",
                    EdgeDistanceCharacterY = "ABCDE",
                    Content = "cesiumcesium测试"
                }
            };
            ImagesHelper.Current.Generation(image);
        }
    }
}
