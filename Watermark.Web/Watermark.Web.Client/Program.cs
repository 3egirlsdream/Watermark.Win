using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Watermark.Razor.Components.Compatibility;
using Watermark.Shared.Models;

namespace Watermark.Web.Client
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.Services.AddWatermarkMasaBlazor();
            builder.Services.AddSingleton<IWMColorEngine>(new WMUnsupportedColorEngine("WebAssembly 不提供原生 OpenColorIO 调色。"));
            builder.Services.AddSingleton<IWMImagingCapabilities>(
                new WMStaticImagingCapabilities(WMImagingCapabilities.Unsupported));
            await builder.Build().RunAsync();
        }
    }
}
