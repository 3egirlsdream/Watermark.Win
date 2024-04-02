using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using MudBlazor;
using Watermark.Andorid.Models;

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

            builder.Services.AddMauiBlazorWebView();
#if ANDROID
            builder.Services.AddSingleton<IUpgradeService, UpgradeService>();
#endif
            builder.Services.AddMudServices();
            builder.Services.AddSingleton<IDialogService, DialogService>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif
            
            return builder.Build();
        }
    }
}
