using NHotkey.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using Watermark.Shared.Models;

namespace Watermark.Win.Models
{
	public class DesignProvider
	{
		public static WMDesignFunc Get(WMCanvas canvas)
		{
			var design = new WMDesignFunc();
			design.CurrentCanvas = canvas;
			design.SelectLogo = new Func<WMLogo, Task>((x) =>
			{
				return Task.Run(() =>
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
			});

			design.SelectContainer = new Func<WMContainer, Task>((x) =>
			{
				return Task.Run(() =>
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
				});
			});

			design.SelectDefaultImageEvt = new Func<Task<string>>(() =>
			{
				return Task.Run(() =>
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
					return p;
				});
			});

			design.ImportFontEvt = new Func<Task>(() =>
			{
				return Task.Run(() =>
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
				});
			});
			design.ImportFontEvt2 = new Func<string, Task>((id) =>
			{
				return Task.Run(() =>
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
								var waterPath = Path.Combine(Global.AppPath.TemplatesFolder, id, Path.GetFileName(f));
								file.CopyTo(waterPath, true);
							}
							catch { }
						}
					}
				});
			});

			design.HotKeyEvt = new Action<Action>((x) =>
			{
				HotkeyManager.Current.AddOrReplace("Increment", Key.R, ModifierKeys.Control, (obj, e) =>
				{
					x.Invoke();
				});
			});
			return design;
		}
	}
}
