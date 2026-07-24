using Watermark.Web.Client.Pages;
using Watermark.Web.Components;
using Watermark.Razor.Components.Compatibility;
using Watermark.Shared.Models;
namespace Watermark.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveWebAssemblyComponents().AddInteractiveServerComponents();
            builder.Services.AddSingleton<APIHelper>();
            builder.Services.AddSingleton<IClientInstance, ClientInstance>();
            builder.Services.AddSingleton<IWMImagingCapabilities>(
                new WMStaticImagingCapabilities(WMImagingCapabilities.Unsupported));
            builder.Services.AddSingleton<IWMPhotoMetadataReader, WMMetadataExtractorReader>();
            builder.Services.AddSingleton<IWMColorEngine>(new WMUnsupportedColorEngine("Web 端不提供原生 OpenColorIO 调色。"));
            builder.Services.AddWatermarkMasaBlazor();
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();
            app.UseAntiforgery();

            app.MapGet("/private", (IWebHostEnvironment environment) =>
            {
                var policyFile = environment.WebRootFileProvider.GetFileInfo(
                    "_content/Watermark.Razor/legal/privacy-policy.html");
                return policyFile.Exists
                    ? Results.Stream(policyFile.CreateReadStream(), "text/html; charset=utf-8")
                    : Results.NotFound();
            });

            app.MapRazorComponents<App>()
                .AddInteractiveWebAssemblyRenderMode()
                .AddInteractiveServerRenderMode()
                .AddAdditionalAssemblies(typeof(Client._Imports).Assembly);

            app.Run();
        }
    }
}
