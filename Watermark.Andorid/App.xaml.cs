using Microsoft.Maui.Controls.PlatformConfiguration;
using Watermark.Shared.Models;

namespace Watermark.Andorid
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            MainPage = new MainPage();
			AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
			{
				try
				{
					var text = error.ExceptionObject.ToString() ?? "";
					var logPath = Path.Combine(Global.AppPath.BasePath, "log");
					if (!Directory.Exists(logPath))
					{
						Directory.CreateDirectory(logPath);
					}
					File.WriteAllText(Path.Combine(logPath, DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt"), text);
				}
				catch { }
			};
		}

		protected override Window CreateWindow(IActivationState? activationState)
		{
			var window = base.CreateWindow(activationState);
#if MACCATALYST
			window.Created += (s, e) =>
			{
				// UIWindowScene.Titlebar 在 Created 时已可用
				WindowHelper.ConfigureTitleBar();

				// NSWindow 需要稍后才能获取到，用 NSTimer 延迟执行
				Foundation.NSTimer.CreateScheduledTimer(TimeSpan.FromMilliseconds(500), _ =>
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						WindowHelper.HideTrafficLights();
						WindowHelper.SetCornerRadius(10.0);
					});
				});
			};
#endif
			return window;
		}

		public static object HttpClientHandler;

    }
}
