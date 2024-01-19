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
