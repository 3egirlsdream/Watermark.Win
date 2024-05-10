using MudBlazor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watermark.Razor
{
    public static class Common
    {
        public static void ShowMsg(ISnackbar snackbar, string message, Severity severity)
        {
            snackbar.Configuration.PositionClass = Defaults.Classes.Position.TopCenter;
            snackbar?.Add(message, severity, config =>
            {
                config.ShowCloseIcon = false;
            });
        }
    }
}
