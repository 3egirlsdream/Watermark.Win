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
					WindowHelper.ConfigureTitleBar();
				};
#endif
			return window;
		}

		public static object HttpClientHandler;

    }
}
