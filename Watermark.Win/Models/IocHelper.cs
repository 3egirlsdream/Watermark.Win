﻿using Masa.Blazor.Popup;
using Masa.Blazor.Presets;
using Masa.Blazor;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Watermark.Win.Views;
using static MudBlazor.CategoryTypes;

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
            _services.AddBlazorWebViewDeveloperTools();
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
