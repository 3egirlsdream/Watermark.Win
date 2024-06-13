using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using Watermark.Andorid.Models;
using Watermark.Shared.Models;
using Watermark.Win.Models;

namespace Watermark.Andorid
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
#if ANDROID
            builder.Services.AddSingleton<IUpgradeService, UpgradeService>();
#endif
            builder.Services.AddSingleton<WMDesignFunc>();
            builder.Services.AddMudServices();
            builder.Services.AddSingleton<IDialogService, DialogService>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton<IWMWatermarkHelper, WatermarkHelper>();
			builder.Services.AddSingleton<APIHelper>();
			return builder.Build();
        }
    }
}
