using Watermark.Web.Client.Pages;
using Watermark.Web.Components;
using Watermark.Razor.Components.Compatibility;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Watermark.Web.Services;
namespace Watermark.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Kestrel 只监听明文 4396，对外统一走 Caddy 终结 TLS 的公共入口。
            APIHelper.HOST = "https://thankful.top";

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveWebAssemblyComponents().AddInteractiveServerComponents();
            builder.Services.AddSingleton<APIHelper>();
            builder.Services.AddScoped<IWMAccountService, WMWebAccountService>();
            builder.Services.AddScoped<IWMExternalActionService, WMWebExternalActionService>();
            builder.Services.AddScoped<IWMNavigationHistory, WMNavigationHistory>();
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

            app.MapGet("/membership-agreement", (IWebHostEnvironment environment) =>
            {
                var agreementFile = environment.WebRootFileProvider.GetFileInfo(
                    "_content/Watermark.Razor/legal/membership-service-agreement.html");
                return agreementFile.Exists
                    ? Results.Stream(agreementFile.CreateReadStream(), "text/html; charset=utf-8")
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
