using CommunityToolkit.Maui;
using Masa.Blazor.Popup;
using Masa.Blazor.Presets;
using Masa.Blazor;
using Microsoft.Extensions.Logging;
using Watermark.Andorid.Models;
using Watermark.Razor.Components.Mac;
using Watermark.Razor.Components.Mac.Editor;
using Watermark.Razor.Components.Mac.Editing;
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
            builder.Services.AddMasaBlazor(options =>
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
            builder.Services.AddScoped<MacTemplateLibraryService>();
            builder.Services.AddScoped<MacTemplateStore>();
            builder.Services.AddSingleton<IWMPhotoMetadataReader, WMMetadataExtractorReader>();
            builder.Services.AddSingleton<IWMSourceStager, WMLocalSourceStager>();
            builder.Services.AddSingleton<WMSkiaPhotoDecoder>();
            builder.Services.AddSingleton<WMNativePhotoDecoder>();
            builder.Services.AddSingleton<IWMFullResolutionTileWarper, WMNativeFullResolutionTileWarper>();
#if MACCATALYST || WINDOWS
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
#else
            builder.Services.AddSingleton<IWMPhotoDecoder>(provider => provider.GetRequiredService<WMSkiaPhotoDecoder>());
            builder.Services.AddSingleton<IWMFrameAligner, WMIdentityFrameAligner>();
            builder.Services.AddSingleton<IWMImagingCapabilities>(
                new WMStaticImagingCapabilities(WMImagingCapabilities.MobileDisabled));
            builder.Services.AddSingleton<IWMTiff16Encoder, WMUnsupportedTiff16Encoder>();
#endif
#if MACCATALYST
            builder.Services.AddScoped<MacEditingSession>();
            builder.Services.AddScoped<MacWorkspaceCoordinator>();
            builder.Services.AddScoped<MacImageImportService>();
            builder.Services.AddScoped<MacColorPreviewValidator>();
            builder.Services.AddScoped<MacFastJpegExportService>();
            builder.Services.AddScoped<MacFullResolutionRenderService>();
            builder.Services.AddSingleton<WMHighPrecisionColorPipeline>();
            builder.Services.AddSingleton<IWMProcessingScheduler, WMProcessingScheduler>();
            builder.Services.AddSingleton<IWMColorLookMapper, WMColorLookMapper>();
            builder.Services.AddSingleton<IWMColorAnalysisService, WMColorAnalysisService>();
            builder.Services.AddSingleton<IWMTemplateRenderer, WMTemplateRenderer>();
            builder.Services.AddSingleton<IWMHighPrecisionTemplateRenderer, WMHighPrecisionTemplateRenderer>();
            builder.Services.AddTransient<WMTemplateOperationProcessor>();
            builder.Services.AddTransient<WMColorGradeOperationProcessor>();
            builder.Services.AddTransient<WMStarTrailOperationProcessor>();
            builder.Services.AddTransient<IWMImageOperationProcessor, WMTemplateOperationProcessor>();
            builder.Services.AddTransient<IWMImageOperationProcessor, WMColorGradeOperationProcessor>();
            builder.Services.AddTransient<IWMImageOperationProcessor, WMStarTrailOperationProcessor>();
#endif

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton<WatermarkHelper>();
            builder.Services.AddSingleton<IWMWatermarkHelper>(provider => provider.GetRequiredService<WatermarkHelper>());
            builder.Services.AddSingleton<APIHelper>();
            Global.DeviceType = Shared.Enums.DeviceType.Andorid;
#if MACCATALYST
            Global.DeviceType = DeviceType.Mac;
#endif
            return builder.Build();
        }
    }
}
