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
			Resources.SetIoc(services);
            InitializeComponent();
        }
        public DesignWin(WMCanvas canvas, string cloud = "")
        {
            var design = new WMDesignFunc();
            design.CurrentCanvas = canvas;
			design.SelectLogo = new Action<WMLogo>((x) =>
			{
				Microsoft.Win32.OpenFileDialog dialog = new()
				{
					DefaultExt = ".png",  // 设置默认类型
					Multiselect = false,                             // 设置可选格式
					Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
				};
				// 打开选择框选择
				var result = dialog.ShowDialog();
				if (result == true)
				{
					var p = dialog.FileName;
					var destFolder = Global.AppPath.TemplatesFolder + canvas.ID;
					if (!System.IO.Directory.Exists(destFolder))
					{
						System.IO.Directory.CreateDirectory(destFolder);
					}
					var name = Path.GetFileName(p);
					var destFile = destFolder + System.IO.Path.DirectorySeparatorChar + name;
					System.IO.File.Copy(p, destFile, true);
					x.Path = name;
				}
			});
			design.SelectContainer = new Func<WMContainer, Task>((x) =>
			{
				Microsoft.Win32.OpenFileDialog dialog = new()
				{
					DefaultExt = ".png",  // 设置默认类型
					Multiselect = false,                             // 设置可选格式
					Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
				};
				// 打开选择框选择
				var result = dialog.ShowDialog();
				if (result == true)
				{
					var p = dialog.FileName;
					x.Path = p;
				}
				return Task.CompletedTask;
			});

			design.SelectDefaultImageEvt = new Func<Task<string>>(() =>
			{
				Microsoft.Win32.OpenFileDialog dialog = new()
				{
					DefaultExt = ".png",  // 设置默认类型
					Multiselect = false,                             // 设置可选格式
					Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
				};
				// 打开选择框选择
				var result = dialog.ShowDialog();
				var p = "";
				if (result == true)
				{
					p = dialog.FileName;
				}

				return Task.Run(() => p);
			});

			design.ImportFontEvt = new Func<Task>(() =>
			{
				var fontPath = Global.AppPath.FontFolder;
                Microsoft.Win32.OpenFileDialog dialog = new()
                {
                    DefaultExt = ".ttf",  // 设置默认类型
                    Multiselect = false,                             // 设置可选格式
                    Filter = @"字体文件(*.ttf,*.otf)|*ttf;*.otf"
                };
                // 打开选择框选择
                var result = dialog.ShowDialog();
				if (result == true)
				{
					var f = dialog.FileName;
					if (!Directory.Exists(fontPath))
					{
						Directory.CreateDirectory(fontPath);
					}
					var file = new FileInfo(f);
					if (file.Exists)
					{
						try
						{
							var target = Path.Combine(fontPath, Path.GetFileName(f));
							file.CopyTo(target, true);
                        }
                        catch { }
						try
						{
							var waterPath = Path.Combine(Global.AppPath.TemplatesFolder, canvas.ID, Path.GetFileName(f));
							file.CopyTo(waterPath, true);
						}
						catch { }
					}
				}
				return Task.CompletedTask;
			});
			design.HotKeyEvt = new Action<Action>((x) =>
			{
				HotkeyManager.Current.AddOrReplace("Increment", Key.R, ModifierKeys.Control, (obj, e) =>
				{
					x.Invoke();
				});
			});
			//design.InitFontEvt = new Action<List<string>>((x) => ClientInstance.InitLocalFontsAction(x));

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
