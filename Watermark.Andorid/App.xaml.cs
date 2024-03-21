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
					var cur = DeviceInfo.Current;
					var device = $"{cur.Model}-{cur.Manufacturer}-{cur.Name}-{cur.VersionString}-{cur.Idiom}-{cur.Platform}";
					var api = new APIHelper();
					var _ = api.UploadLog(device, text);
				}
				catch { }
			};
		}

		public static object HttpClientHandler;

    }
}
