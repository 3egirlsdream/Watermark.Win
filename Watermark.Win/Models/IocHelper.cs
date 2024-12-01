using Masa.Blazor;
using Masa.Blazor.Popup;
using Masa.Blazor.Presets;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using System.Windows;

namespace Watermark.Win.Models
{
    public static class IocHelper
    {
        public const string IocKey = "services";

        private static ServiceCollection _services = null;

        public static ServiceCollection GetIoc()
        {
            if (_services != null)
            {
                return _services!;
            }

            _services = new ServiceCollection();
            _services.AddMudServices();
            _services.AddWpfBlazorWebView();
#if DEBUG
            _services.AddBlazorWebViewDeveloperTools();
#endif
            _services.AddMasaBlazor(options =>
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
                            { nameof(PEnqueuedSnackbars.Position), SnackPosition.TopCenter },
                            { nameof(PEnqueuedSnackbars.MaxCount), 1}
                        }
                    }
                };
            }, ServiceLifetime.Scoped);

            return _services!;
        }

        public static void SetIoc(this ResourceDictionary resourceDictionary, ServiceCollection services)
        {
            if (!resourceDictionary.Contains(IocKey))
            {
                resourceDictionary.Add(IocKey, services.BuildServiceProvider());
            }
        }

        public static void SetIoc(this ResourceDictionary resourceDictionary)
        {
            var service = GetIoc();
            resourceDictionary.SetIoc(service);
        }
    }
}
