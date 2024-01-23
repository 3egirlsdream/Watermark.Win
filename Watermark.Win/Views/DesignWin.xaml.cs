using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SkiaSharp;
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
