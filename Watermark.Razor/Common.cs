#nullable enable

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
        /// <summary>
        /// Displays a transient, non-blocking application notification.
        /// Informational and successful outcomes use the neutral dark toast;
        /// failures retain the error treatment.
        /// </summary>
        public static void ShowToast(IPopupService popupService, string? message, bool isError = false)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var normalizedMessage = message.Trim();
            _ = isError
                ? popupService.EnqueueSnackbarAsync(normalizedMessage, AlertTypes.Error)
                : popupService.EnqueueSnackbarAsync(new SnackbarOptions(normalizedMessage));
        }

        public static void ShowToast(
            IPopupService popupService,
            string? message,
            string actionName,
            Func<Task> action)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            _ = popupService.EnqueueSnackbarAsync(
                new SnackbarOptions(message.Trim(), actionName, action));
        }

        public static void ShowMsg(IPopupService PopupService, string message, AlertTypes _alertType)
        {
            ShowToast(PopupService, message, _alertType == AlertTypes.Error);
        }

        public static void ShowMsg(IPopupService PopupService, string message, string actionName, Func<Task> func)
        {
            ShowToast(PopupService, message, actionName, func);
        }
        
        
    }
}
