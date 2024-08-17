using Masa.Blazor.Popup;
using Masa.Blazor.Presets;
using Masa.Blazor;
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
            builder.Services.AddMasaBlazor(options =>
            {
                options.Defaults = new Dictionary<string, IDictionary<string, object?>?>()
                {
                    {
                        PopupComponents.CONFIRM, new Dictionary<string, object?>()
                        {
                            {
                                nameof(PromptOptions.OkProps), (Action<ModalButtonProps>)(u =>
                                {
                                    u.Class = "text-capitalize";
                                    u.Text = false;
                                })
                            },
                            { nameof(ConfirmOptions.CancelProps), (Action<ModalButtonProps>)(u => u.Class = "text-capitalize") },
                        }
                    },
                    {
                        PopupComponents.SNACKBAR, new Dictionary<string, object?>()
                        {
                            { nameof(PEnqueuedSnackbars.Closeable), true },
                            { nameof(PEnqueuedSnackbars.Position), SnackPosition.TopRight }
                        }
                    }
                };
            });
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
