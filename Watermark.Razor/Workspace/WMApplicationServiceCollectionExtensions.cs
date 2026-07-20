using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<WMTemplateDesignerSession>();
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
