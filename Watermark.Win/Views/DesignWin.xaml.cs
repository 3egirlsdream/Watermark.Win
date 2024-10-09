using Microsoft.Extensions.DependencyInjection;
using NHotkey.Wpf;
using SkiaSharp;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Watermark.Shared.Models;
using Watermark.Win.Models;

namespace Watermark.Win.Views
{
    /// <summary>
    /// DesignWin.xaml 的交互逻辑
    /// </summary>
    public partial class DesignWin : Window
    {
        public DesignWin()
        {
            var services = IocHelper.GetIoc();
            services.AddSingleton(new WMCanvas());
            services.AddSingleton("");
			services.AddSingleton<IWMWatermarkHelper, WatermarkHelper>();

            var design = new WMDesignFunc();
			design.CurrentCanvas = new WMCanvas();
            services.AddSingleton(design);
            Resources.SetIoc(services);
            InitializeComponent();
        }
        public DesignWin(WMCanvas canvas, string cloud = "")
        {

            //design.InitFontEvt = new Action<List<string>>((x) => ClientInstance.InitLocalFontsAction(x));
            var design = DesignProvider.Get(canvas);
			var services = IocHelper.GetIoc();
			services.AddSingleton<IWMWatermarkHelper, WatermarkHelper>();
			services.AddSingleton(canvas);
            services.AddSingleton(cloud);
			services.AddSingleton(design);
            Resources.SetIoc(services);
            InitializeComponent();
        }
    }
}
