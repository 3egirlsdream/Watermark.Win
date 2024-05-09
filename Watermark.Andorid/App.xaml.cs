using Microsoft.Maui.Controls.PlatformConfiguration;
using Watermark.Win.Models;

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
					if (Directory.Exists(Global.AppPath.BasePath + "log"))
					{
						Directory.CreateDirectory(Global.AppPath.BasePath + "log");

					}
					File.WriteAllText(Global.AppPath.BasePath + "log" + Path.DirectorySeparatorChar + DateTime.Now.ToString(), text);
				}
				catch { }
			};
		}

		public static object HttpClientHandler;

    }
}
