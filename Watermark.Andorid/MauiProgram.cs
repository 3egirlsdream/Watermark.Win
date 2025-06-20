﻿using Masa.Blazor.Popup;
using Masa.Blazor.Presets;
using Masa.Blazor;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using Watermark.Andorid.Models;
using Watermark.Shared.Models;
using DeviceType = Watermark.Shared.Enums.DeviceType;

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
                            { nameof(PromptOptions.OkText), "确定" },
                            { nameof(ConfirmOptions.CancelProps), (Action<ModalButtonProps>)(u => u.Class = "text-capitalize") },
                            { nameof(ConfirmOptions.CancelText), "取消" }
                        }
                    },
                    {
                        PopupComponents.SNACKBAR, new Dictionary<string, object?>()
                        {
                            { nameof(PEnqueuedSnackbars.Closeable), false },
                            { nameof(PEnqueuedSnackbars.Position), SnackPosition.BottomCenter },
                            { nameof(PEnqueuedSnackbars.MaxCount), 1}
                        }
                    }
                };
            }, ServiceLifetime.Scoped);
            builder.Services.AddSingleton<IUpgradeService, UpgradeService>();
            builder.Services.AddSingleton<WMDesignFunc>();
            builder.Services.AddMudServices();
            builder.Services.AddSingleton<IDialogService, DialogService>();
            builder.Services.AddSingleton<IClientInstance, ClientInstance>();
            builder.Services.AddSingleton<LoadingService>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton<IWMWatermarkHelper, WatermarkHelper>();
            builder.Services.AddSingleton<APIHelper>();
            Global.DeviceType = Shared.Enums.DeviceType.Andorid;
#if MACCATALYST
            Global.DeviceType = DeviceType.Mac;
#endif
            return builder.Build();
        }
    }
}
