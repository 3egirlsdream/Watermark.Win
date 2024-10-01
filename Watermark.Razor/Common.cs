using Masa.Blazor;
using Masa.Blazor.Presets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watermark.Razor
{
    public static class Common
    {
        public static void ShowMsg(MudBlazor.ISnackbar snackbar, string message, MudBlazor.Severity severity)
        {
            snackbar.Configuration.PositionClass = MudBlazor.Defaults.Classes.Position.TopCenter;
            snackbar?.Add(message, severity, config =>
            {
                config.ShowCloseIcon = false;
            });
        }

        public static void ShowMsg(IPopupService PopupService, string message, AlertTypes _alertType)
        {
            var _ = PopupService.EnqueueSnackbarAsync(message, _alertType);
        }

        public static void ShowMsg(IPopupService PopupService, string message, string actionName, Func<Task> func)
        {
            var _ = PopupService.EnqueueSnackbarAsync(new SnackbarOptions(message, actionName, func));
        }
        
        
    }
}
