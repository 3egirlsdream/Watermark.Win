using CommunityToolkit.Maui;
using Masa.Blazor.Popup;
using Masa.Blazor.Presets;
using Masa.Blazor;
using Microsoft.Extensions.Logging;
using Watermark.Andorid.Models;
using Watermark.Razor.Components.Compatibility;
using Watermark.Razor.Components.Mac;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using DeviceType = Watermark.Shared.Enums.DeviceType;

namespace Watermark.Andorid
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });


            builder.Services.AddFilePicker();
            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddWatermarkMasaBlazor(options =>
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
                            { nameof(PEnqueuedSnackbars.Position), SnackPosition.BottomCenter },
                            { nameof(PEnqueuedSnackbars.MaxCount), 1}
                        }
                    }
                };
            }, ServiceLifetime.Scoped);
            builder.Services.AddSingleton<IUpgradeService, UpgradeService>();
            builder.Services.AddSingleton<WMDesignFunc>();
            builder.Services.AddSingleton<IClientInstance, ClientInstance>();
            builder.Services.AddSingleton<LoadingService>();
            builder.Services.AddScoped<WMTemplateLibraryService>();
            builder.Services.AddScoped<WMTemplateStore>();
            builder.Services.AddSingleton<IWMPhotoMetadataReader, WMMetadataExtractorReader>();
            builder.Services.AddSingleton<IWMSourceStager>(new WMLocalSourceStager(copyLocalSources: true));
            builder.Services.AddSingleton<WMSkiaPhotoDecoder>();
            builder.Services.AddSingleton<WMNativePhotoDecoder>();
            builder.Services.AddSingleton<IWMFullResolutionTileWarper, WMNativeFullResolutionTileWarper>();
#if MACCATALYST || WINDOWS || IOS
            builder.Services.AddSingleton<IWMPreviewFrameDecoder, WMCompositePreviewFrameDecoder>();
            builder.Services.AddSingleton<IWMStarFeatureAnalyzer, WMNativeStarFeatureAnalyzer>();
            builder.Services.AddSingleton<IWMPreviewFrameWarper, WMNativePreviewFrameWarper>();
            builder.Services.AddSingleton<IWMPreviewStackComposer, WMNativePreviewStackComposer>();
            builder.Services.AddSingleton<IWMMultiFramePreviewEngine, WMMultiFramePreviewEngine>();
            builder.Services.AddSingleton<IWMPhotoDecoder, WMCompositePhotoDecoder>();
            builder.Services.AddSingleton<IWMFrameAligner, WMNativeFrameAligner>();
            builder.Services.AddSingleton<IWMImagingCapabilities, WMNativeImagingCapabilities>();
            builder.Services.AddSingleton<IWMTiff16Encoder, WMNativeTiff16Encoder>();
            builder.Services.AddSingleton<IWMImageStackEngine, WMMultiFrameStackEngine>();
            builder.Services.AddTransient<WMMultiFrameStackOperationProcessor>();
            builder.Services.AddTransient<IWMImageOperationProcessor, WMMultiFrameStackOperationProcessor>();
#elif ANDROID
            // Register the native graph for hidden diagnostics. The Android
            // capability provider below keeps every product entry gated by
            // backend symbols, resources and independent local QA flags.
            builder.Services.AddSingleton<IWMPreviewFrameDecoder, WMCompositePreviewFrameDecoder>();
            builder.Services.AddSingleton<IWMStarFeatureAnalyzer, WMNativeStarFeatureAnalyzer>();
            builder.Services.AddSingleton<IWMPreviewFrameWarper, WMNativePreviewFrameWarper>();
            builder.Services.AddSingleton<IWMPreviewStackComposer, WMNativePreviewStackComposer>();
            builder.Services.AddSingleton<IWMMultiFramePreviewEngine, WMMultiFramePreviewEngine>();
            builder.Services.AddSingleton<WMCompositePhotoDecoder>();
            builder.Services.AddSingleton<IWMPhotoDecoder, WMAndroidCapabilityGatedPhotoDecoder>();
            builder.Services.AddSingleton<IWMFrameAligner, WMNativeFrameAligner>();
            builder.Services.AddSingleton<WMNativeImagingCapabilities>();
            builder.Services.AddSingleton<WMAndroidImagingCapabilityProvider>();
            builder.Services.AddSingleton<IWMImagingCapabilityProvider>(provider =>
                provider.GetRequiredService<WMAndroidImagingCapabilityProvider>());
            builder.Services.AddSingleton<IWMImagingCapabilities>(provider =>
                provider.GetRequiredService<WMAndroidImagingCapabilityProvider>());
            builder.Services.AddSingleton<IWMImagingDiagnosticsService, WMAndroidImagingDiagnosticsService>();
            builder.Services.AddSingleton<IWMTiff16Encoder, WMNativeTiff16Encoder>();
            builder.Services.AddSingleton<IWMImageStackEngine, WMMultiFrameStackEngine>();
            builder.Services.AddTransient<WMMultiFrameStackOperationProcessor>();
            builder.Services.AddTransient<IWMImageOperationProcessor, WMMultiFrameStackOperationProcessor>();
#else
            builder.Services.AddSingleton<IWMPhotoDecoder>(provider => provider.GetRequiredService<WMSkiaPhotoDecoder>());
            builder.Services.AddSingleton<IWMFrameAligner, WMIdentityFrameAligner>();
            builder.Services.AddSingleton<IWMImagingCapabilities>(
                new WMStaticImagingCapabilities(WMImagingCapabilities.MobileDisabled));
            builder.Services.AddSingleton<IWMTiff16Encoder, WMUnsupportedTiff16Encoder>();
            builder.Services.AddSingleton<IWMImagingDiagnosticsService, WMImagingDiagnosticsService>();
#endif
#if !ANDROID
            builder.Services.AddSingleton<IWMImagingCapabilityProvider, WMHostImagingCapabilityProvider>();
#endif

            // Shared workspace services are available on every host. Heavy imaging
            // capabilities remain platform-gated below and through feature flags.
            builder.Services.AddSingleton<IWMWorkspacePerformanceCounters, WMWorkspacePerformanceCounters>();
            builder.Services.AddSingleton<WMWorkspaceTraceStore>();
            builder.Services.AddSingleton<IWMWorkspaceTraceStore>(provider =>
                provider.GetRequiredService<WMWorkspaceTraceStore>());
            builder.Services.AddSingleton<ILoggerProvider, WMDiagnosticLoggerProvider>();
            builder.Services.AddSingleton<IWMArtifactCache, WMArtifactCache>();
            builder.Services.AddSingleton<IWMProcessingScheduler, WMProcessingScheduler>();
            builder.Services.AddSingleton<IWMColorEngine, WMOcioColorEngine>();
            builder.Services.AddSingleton<WMHighPrecisionColorPipeline>();
            builder.Services.AddSingleton<IWMColorLookMapper, WMColorLookMapper>();
            builder.Services.AddSingleton<IWMColorAnalysisService, WMColorAnalysisService>();
            builder.Services.AddSingleton<IWMRenderPlanCompiler, WMRenderPlanCompiler>();
            builder.Services.AddScoped<IWMRenderExecutor, WMRenderExecutor>();
            builder.Services.AddSingleton<IWMColorPipelineCompiler, WMColorPipelineCompiler>();
            builder.Services.AddSingleton<WMColorPreviewValidator>();
            builder.Services.AddSingleton<IWMTemplateRenderer, WMTemplateRenderer>();
            builder.Services.AddSingleton<IWMHighPrecisionTemplateRenderer, WMHighPrecisionTemplateRenderer>();
            builder.Services.AddTransient<WMTemplateOperationProcessor>();
            builder.Services.AddTransient<WMColorGradeOperationProcessor>();
            builder.Services.AddTransient<IWMImageOperationProcessor, WMTemplateOperationProcessor>();
            builder.Services.AddTransient<IWMImageOperationProcessor, WMColorGradeOperationProcessor>();
            builder.Services.AddScoped<WMFastJpegExportService>();
            builder.Services.AddScoped<WMImageImportService>();
            builder.Services.AddScoped<IWMColorReferenceService, WMColorReferenceService>();
            builder.Services.AddScoped<IWMExecutionProfileProvider, WMExecutionProfileProvider>();
            builder.Services.AddScoped<IWMObjectUrlRegistry, WMObjectUrlRegistry>();
            builder.Services.AddScoped<WMWorkspaceRenderCoordinator>();
            builder.Services.AddScoped<IWMWorkspaceRenderCoordinator>(provider =>
                provider.GetRequiredService<WMWorkspaceRenderCoordinator>());
            builder.Services.AddScoped<WMWorkspaceSessionStore>();
            builder.Services.AddScoped<IWMWorkspaceSessionStore>(provider =>
                provider.GetRequiredService<WMWorkspaceSessionStore>());
            builder.Services.AddScoped<IWMWorkspaceLauncher, WMWorkspaceLauncher>();
            builder.Services.AddScoped<WMWorkspacePreviewService>();
            builder.Services.AddScoped<WMFullResolutionRenderService>();
            builder.Services.AddScoped<WMFullResolutionRenderPipeline>();
            builder.Services.AddScoped<IWMDerivedMediaProcessor, WMCollageDerivedMediaProcessor>();
            builder.Services.AddScoped<IWMColorPresetLibrary, WMColorPresetLibrary>();
            builder.Services.AddScoped<WMTemplateSnapshotService>();
            builder.Services.AddScoped<WMWorkspaceController>();
#if ANDROID
            builder.Services.AddScoped<IWMPhotoPicker, WMAndroidPhotoPicker>();
            builder.Services.AddScoped<IWMExportSink, WMAndroidExportSink>();
            builder.Services.AddScoped<IWMDiagnosticReportExporter, WMAndroidDiagnosticReportExporter>();
#else
            builder.Services.AddScoped<IWMPhotoPicker, WMMauiPhotoPicker>();
            builder.Services.AddScoped<IWMExportSink, WMLocalExportSink>();
            builder.Services.AddScoped<IWMDiagnosticReportExporter, WMMauiDiagnosticReportExporter>();
#endif
            builder.Services.AddSingleton(WMImagingRolloutDefaults.Create());
            builder.Services.AddSingleton<IWMWorkspaceFeatureFlags, WMWorkspaceFeatureFlags>();
            builder.Services.AddSingleton<IWMSystemBackDispatcher, WMSystemBackDispatcher>();
            builder.Services.AddScoped<IWMHapticFeedback, WMClientHapticFeedback>();
            builder.Services.AddScoped<IWMSystemAppearance, WMClientSystemAppearance>();
            builder.Services.AddScoped<IWMTemplateMarketplaceService, WMTemplateMarketplaceService>();
            builder.Services.AddWMApplicationServices();
#if MACCATALYST
            builder.Services.AddTransient<WMStarTrailOperationProcessor>();
            builder.Services.AddTransient<IWMImageOperationProcessor, WMStarTrailOperationProcessor>();
#endif

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton<WatermarkHelper>();
            builder.Services.AddSingleton<IWMWatermarkHelper>(provider => provider.GetRequiredService<WatermarkHelper>());
            builder.Services.AddSingleton<APIHelper>();
#if MACCATALYST
            Global.DeviceType = DeviceType.Mac;
#elif IOS
            Global.DeviceType = DeviceType.IOS;
#elif WINDOWS
            Global.DeviceType = DeviceType.Win;
#else
            Global.DeviceType = DeviceType.Andorid;
#endif
            var app = builder.Build();
            WMDiagnosticUnhandledExceptionRegistration.Register(
                app.Services.GetRequiredService<IWMWorkspaceTraceStore>());
            return app;
        }
    }
}
