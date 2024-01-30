using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;

namespace Watermark.Web.Client
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);

            builder.Services.AddScoped(sp => new HttpClient() { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

            builder.Services.AddMudServices();
            await builder.Build().RunAsync();
        }
    }
}
