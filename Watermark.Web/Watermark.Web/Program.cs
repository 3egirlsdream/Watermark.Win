using Watermark.Web.Client.Pages;
using Watermark.Web.Components;
using MudBlazor;
using MudBlazor.Services;
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
            builder.Services.AddMudServices();
            builder.Services.AddSingleton<IDialogService, DialogService>();
            builder.Services.AddSingleton<APIHelper>();
            builder.Services.AddSingleton<IClientInstance, ClientInstance>();
            builder.Services.AddMasaBlazor();
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

            app.MapRazorComponents<App>()
                .AddInteractiveWebAssemblyRenderMode()
                .AddInteractiveServerRenderMode()
                .AddAdditionalAssemblies(typeof(Client._Imports).Assembly);

            app.Run();
        }
    }
}
