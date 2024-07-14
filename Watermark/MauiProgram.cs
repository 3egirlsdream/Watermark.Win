using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using MudBlazor;
using Watermark.Shared.Models;
using Watermark.Win.Models;

namespace Watermark
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

			builder.Services.AddFilePicker();
			builder.Services.AddMauiBlazorWebView();
			builder.Services.AddMasaBlazor();

#if DEBUG
			builder.Services.AddSingleton<WMDesignFunc>();
			builder.Services.AddMudServices();
			builder.Services.AddSingleton<IDialogService, DialogService>();
			builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

			builder.Services.AddSingleton<IWMWatermarkHelper, WatermarkHelper>();
			builder.Services.AddSingleton<APIHelper>();
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
			return builder.Build();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            throw new NotImplementedException();
        }
    }
}
