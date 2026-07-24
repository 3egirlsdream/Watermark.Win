using Microsoft.Extensions.DependencyInjection;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public static class WMApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddWMApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IWMHostNavigationBridge, WMHostNavigationBridge>();
        services.AddScoped<IWMNavigationHistory, WMNavigationHistory>();
        services.AddScoped<IWMAccountService, WMAccountService>();
        services.AddScoped<IWMApplicationStartupService, WMApplicationStartupService>();
        services.AddScoped<IWMDesktopStartupService, WMDesktopStartupService>();
        services.AddSingleton<IWMDesignSceneRenderer, WMDesignSceneRenderer>();
        services.AddScoped(static provider =>
        {
            var renderer = provider.GetRequiredService<IWMDesignSceneRenderer>();
            var objectUrls = provider.GetRequiredService<IWMObjectUrlRegistry>();
            var transport = provider.GetRequiredService<IWMSceneSurfaceTransport>();
            var metrics = provider.GetService<IWMWorkspacePerformanceCounters>();
            return metrics is null
                ? new WMTemplateDesignerSession(renderer, objectUrls, transport)
                : new WMTemplateDesignerSession(renderer, objectUrls, transport, metrics);
        });
        services.AddScoped<IWMEditorInteractionProfileProvider, WMEditorInteractionProfileProvider>();
        services.AddScoped<IWMSceneSurfaceTransport, WMSceneSurfaceTransport>();
        services.AddScoped<IWMAppSettingsService, WMAppSettingsService>();
        services.AddScoped<IWMCacheMaintenanceService, WMCacheMaintenanceService>();
        services.AddScoped<IWMResourceLibraryService, WMResourceLibraryService>();
        services.AddScoped<IWMAppUpdateService, WMAppUpdateService>();
        services.AddScoped<IWMExternalActionService, WMExternalActionService>();
        services.AddScoped<IWMMembershipPaymentGateway, WMMembershipPaymentGateway>();
        services.AddScoped<IWMAlipayAppLauncher, WMClientAlipayAppLauncher>();
        services.AddScoped<IWMPendingMembershipStore, WMPendingMembershipStore>();
        services.AddSingleton<IWMMembershipPaymentClock, WMSystemMembershipPaymentClock>();
        services.AddScoped<IWMMembershipService, WMMembershipService>();
        services.AddScoped<IWMAdminDashboardService, WMAdminDashboardService>();
        services.AddScoped<WMTemplateMarketFeedStore>();
        return services;
    }
}
