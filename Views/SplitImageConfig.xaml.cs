using JointWatermark.Class;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace JointWatermark.Views
{
    /// <summary>
    /// SplitImageConfig.xaml 的交互逻辑
    /// </summary>
    public partial class SplitImageConfig : Page
    {
        ObservableCollection<ImageProperties> properties;
        MainPage parent;
        public SplitImageConfig(ObservableCollection<ImageProperties> _properties, MainPage page)
        {
            parent = page;
            properties = _properties;
            InitializeComponent();
            Button_Click(null, null);
        }

        public dynamic GetConfig()
        {
            return new
            {
                Index = colCbx.SelectedIndex,
                Border = split_border.Value,
                Quality = quality.Value
            };
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                parent.vm.BottomProcess = new BottomProcessInstance(Visibility.Visible, true);
                var cfg = GetConfig();
                foreach (var im in properties)
                {
                    im.Config.BorderWidth = (int)cfg.Border;
                }

                if (cfg.Index == 0)
                {
                    int border = (int)cfg.Border;
                    var col = (int)Math.Sqrt(properties.Count);
                    var bit = await ImagesHelper.Current.SplitImages2(properties, null, null, border, col, true);
                    var bmp = ImagesHelper.Current.ImageSharpToImageSource(bit);
                    parent.createdImg.Source = bmp;
                    bit.Dispose();
                }
                else if (cfg.Index == 1)
                {
                    var bit = await ImagesHelper.Current.SplitImages(properties, false, null, null, true);
                    var bmp = ImagesHelper.Current.ImageSharpToImageSource(bit);
                    parent.createdImg.Source = bmp;
                    bit.Dispose();
                }
                else if (cfg.Index == 2)
                {
                    var bit = await ImagesHelper.Current.SplitImages(properties, true, null, null, true);
                    var bmp = ImagesHelper.Current.ImageSharpToImageSource(bit);
                    parent.createdImg.Source = bmp;
                    bit.Dispose();
                }
                else
                {
                    var index = cfg.Index;
                    var bit = await ImagesHelper.Current.SplitImages2(properties, null, null, (int)cfg.Border, index, true);
                    var bmp = ImagesHelper.Current.ImageSharpToImageSource(bit);
                    parent.createdImg.Source = bmp;
                    bit.Dispose();
                }

                parent.vm.BottomProcess = new BottomProcessInstance(Visibility.Hidden, false);
            }
            catch(Exception ex)
            {
                Global.SendMsg(ex.Message);
            }
            finally
            {
                parent.vm.BottomProcess = new BottomProcessInstance(Visibility.Hidden, false);
            }
        }
    }
}
