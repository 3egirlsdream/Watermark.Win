using Microsoft.Extensions.DependencyInjection;
using System.Windows;
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
            Resources.SetIoc(services);
            InitializeComponent();
        }
        public DesignWin(WMCanvas canvas, string cloud = "")
        {
            var services = IocHelper.GetIoc();
            services.AddSingleton(canvas);
            services.AddSingleton(cloud);
            Resources.SetIoc(services);
            InitializeComponent();
        }
    }
}
