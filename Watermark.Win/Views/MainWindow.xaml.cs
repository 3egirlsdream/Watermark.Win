using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Watermark.Win.Models;
using Watermark.Win.Views;

namespace Watermark.Win
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool updateCheckStarted;

        public MainWindow()
        {
            try
            {
				WMWindowsNativeLibraryLoader.Register();
				Global.DeviceType = Watermark.Shared.Enums.DeviceType.Win;
				IocHelper.GetIoc().AddSingleton<WatermarkHelper>();
                IocHelper.GetIoc().AddSingleton<IWMWatermarkHelper>(provider => provider.GetRequiredService<WatermarkHelper>());
				IocHelper.GetIoc().AddSingleton<APIHelper>();
                IocHelper.GetIoc().AddSingleton<IWindowService, WindowService>();
                IocHelper.GetIoc().AddScoped<MainInterop>();
                IocHelper.GetIoc().AddSingleton<IClientInstance, ClientInstance>();
                IocHelper.GetIoc().AddSingleton<IWMPhotoMetadataReader, WMMetadataExtractorReader>();
                IocHelper.GetIoc().AddSingleton<IWMSourceStager>(
                    new WMLocalSourceStager(copyLocalSources: true));
                IocHelper.GetIoc().AddSingleton<WMSkiaPhotoDecoder>();
                IocHelper.GetIoc().AddSingleton<WMNativePhotoDecoder>();
                IocHelper.GetIoc().AddSingleton<IWMPhotoDecoder, WMCompositePhotoDecoder>();
                IocHelper.GetIoc().AddSingleton<IWMFrameAligner, WMNativeFrameAligner>();
                IocHelper.GetIoc().AddSingleton<IWMFullResolutionTileWarper, WMNativeFullResolutionTileWarper>();
                IocHelper.GetIoc().AddSingleton<IWMPreviewFrameDecoder, WMCompositePreviewFrameDecoder>();
                IocHelper.GetIoc().AddSingleton<IWMStarFeatureAnalyzer, WMNativeStarFeatureAnalyzer>();
                IocHelper.GetIoc().AddSingleton<IWMPreviewFrameWarper, WMNativePreviewFrameWarper>();
                IocHelper.GetIoc().AddSingleton<IWMPreviewStackComposer, WMNativePreviewStackComposer>();
                IocHelper.GetIoc().AddSingleton<IWMMultiFramePreviewEngine, WMMultiFramePreviewEngine>();
                IocHelper.GetIoc().AddSingleton<IWMImagingCapabilities, WMNativeImagingCapabilities>();
                IocHelper.GetIoc().AddSingleton<IWMTiff16Encoder, WMNativeTiff16Encoder>();
                IocHelper.GetIoc().AddSingleton<IWMImageStackEngine, WMMultiFrameStackEngine>();
                IocHelper.GetIoc().AddSingleton<IWMHighPrecisionTemplateRenderer, WMHighPrecisionTemplateRenderer>();
                IocHelper.GetIoc().AddTransient<WMMultiFrameStackOperationProcessor>();
                IocHelper.GetIoc().AddTransient<IWMImageOperationProcessor, WMMultiFrameStackOperationProcessor>();
                IocHelper.GetIoc().AddSingleton<WMDesignFunc>();
                IocHelper.GetIoc().AddSingleton<LoadingService>();
                IocHelper.GetIoc().AddScoped<WMTemplateLibraryService>();
                IocHelper.GetIoc().AddScoped<WMTemplateStore>();
                IocHelper.GetIoc().AddSingleton<IWMWorkspacePerformanceCounters, WMWorkspacePerformanceCounters>();
                IocHelper.GetIoc().AddSingleton<WMWorkspaceTraceStore>();
                IocHelper.GetIoc().AddSingleton<IWMWorkspaceTraceStore>(provider =>
                    provider.GetRequiredService<WMWorkspaceTraceStore>());
                IocHelper.GetIoc().AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider, WMDiagnosticLoggerProvider>();
                IocHelper.GetIoc().AddSingleton<IWMArtifactCache, WMArtifactCache>();
                IocHelper.GetIoc().AddSingleton<IWMProcessingScheduler, WMProcessingScheduler>();
                IocHelper.GetIoc().AddSingleton<IWMColorEngine, WMOcioColorEngine>();
                IocHelper.GetIoc().AddSingleton<WMHighPrecisionColorPipeline>();
                IocHelper.GetIoc().AddSingleton<IWMColorLookMapper, WMColorLookMapper>();
                IocHelper.GetIoc().AddSingleton<IWMColorAnalysisService, WMColorAnalysisService>();
                IocHelper.GetIoc().AddSingleton<IWMRenderPlanCompiler, WMRenderPlanCompiler>();
                IocHelper.GetIoc().AddScoped<IWMRenderExecutor, WMRenderExecutor>();
                IocHelper.GetIoc().AddSingleton<IWMColorPipelineCompiler, WMColorPipelineCompiler>();
                IocHelper.GetIoc().AddSingleton<WMColorPreviewValidator>();
                IocHelper.GetIoc().AddSingleton<IWMTemplateRenderer, WMTemplateRenderer>();
                IocHelper.GetIoc().AddTransient<WMTemplateOperationProcessor>();
                IocHelper.GetIoc().AddTransient<WMColorGradeOperationProcessor>();
                IocHelper.GetIoc().AddTransient<IWMImageOperationProcessor, WMTemplateOperationProcessor>();
                IocHelper.GetIoc().AddTransient<IWMImageOperationProcessor, WMColorGradeOperationProcessor>();
                IocHelper.GetIoc().AddTransient<WMStarTrailOperationProcessor>();
                IocHelper.GetIoc().AddTransient<IWMImageOperationProcessor, WMStarTrailOperationProcessor>();
                IocHelper.GetIoc().AddScoped<WMFastJpegExportService>();
                IocHelper.GetIoc().AddScoped<WMImageImportService>();
                IocHelper.GetIoc().AddScoped<IWMColorReferenceService, WMColorReferenceService>();
                IocHelper.GetIoc().AddScoped<IWMExecutionProfileProvider, WMExecutionProfileProvider>();
                IocHelper.GetIoc().AddScoped<IWMObjectUrlRegistry, WMObjectUrlRegistry>();
                IocHelper.GetIoc().AddScoped<WMWorkspaceRenderCoordinator>();
                IocHelper.GetIoc().AddScoped<IWMWorkspaceRenderCoordinator>(provider =>
                    provider.GetRequiredService<WMWorkspaceRenderCoordinator>());
                IocHelper.GetIoc().AddScoped<WMWorkspaceSessionStore>();
                IocHelper.GetIoc().AddScoped<IWMWorkspaceSessionStore>(provider =>
                    provider.GetRequiredService<WMWorkspaceSessionStore>());
                IocHelper.GetIoc().AddScoped<IWMWorkspaceLauncher, WMWorkspaceLauncher>();
                IocHelper.GetIoc().AddScoped<WMWorkspacePreviewService>();
                IocHelper.GetIoc().AddScoped<WMFullResolutionRenderService>();
                IocHelper.GetIoc().AddScoped<WMFullResolutionRenderPipeline>();
                IocHelper.GetIoc().AddScoped<IWMDerivedMediaProcessor, WMCollageDerivedMediaProcessor>();
                IocHelper.GetIoc().AddScoped<IWMPosterApplicationProcessor, WMPosterApplicationProcessor>();
                IocHelper.GetIoc().AddScoped<IWMColorPresetLibrary, WMColorPresetLibrary>();
                IocHelper.GetIoc().AddScoped<WMTemplateSnapshotService>();
                IocHelper.GetIoc().AddScoped<WMWorkspaceController>();
                IocHelper.GetIoc().AddScoped<IWMPhotoPicker, WMWpfPhotoPicker>();
                IocHelper.GetIoc().AddScoped<IWMExportSink, WMLocalExportSink>();
                IocHelper.GetIoc().AddScoped<IWMDiagnosticReportExporter, WMLocalDiagnosticReportExporter>();
                IocHelper.GetIoc().AddSingleton<IWMImagingCapabilityProvider, WMHostImagingCapabilityProvider>();
                IocHelper.GetIoc().AddSingleton<IWMImagingDiagnosticsService, WMImagingDiagnosticsService>();
                IocHelper.GetIoc().AddSingleton(WMImagingRolloutDefaults.Create());
                IocHelper.GetIoc().AddSingleton<IWMWorkspaceFeatureFlags, WMWorkspaceFeatureFlags>();
                IocHelper.GetIoc().AddSingleton<IWMSystemBackDispatcher, WMSystemBackDispatcher>();
                IocHelper.GetIoc().AddScoped<IWMHapticFeedback, WMClientHapticFeedback>();
                IocHelper.GetIoc().AddScoped<IWMSystemAppearance, WMClientSystemAppearance>();
                IocHelper.GetIoc().AddScoped<IWMTemplateMarketplaceService, WMTemplateMarketplaceService>();
				IocHelper.GetIoc().AddWMApplicationServices();
				Resources.SetIoc();
                if (Resources[IocHelper.IocKey] is IServiceProvider serviceProvider)
                {
                    WMDiagnosticUnhandledExceptionRegistration.Register(
                        serviceProvider.GetRequiredService<IWMWorkspaceTraceStore>());
                }
                InitializeComponent();
                Loaded += MainWindow_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (updateCheckStarted) return;
            updateCheckStarted = true;
            await CheckUpdateAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                var path = Global.AppPath.ThumbnailFolder;
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
                // A preview can still be releasing a file handle during shutdown.
            }
            base.OnClosed(e);
        }

        public void CheckUpdate() => _ = CheckUpdateAsync();

        public async Task CheckUpdateAsync()
        {
            var day = DateTime.Now.DayOfYear;
            if (day % 3 != 0) return;
            if (Resources[IocHelper.IocKey] is not IServiceProvider services) return;

            try
            {
                var client = services.GetRequiredService<IClientInstance>();
                if (!await client.CheckUpdate("WatermarkV3")) return;

                var updateWindow = new UpdateWin
                {
                    ShowInTaskbar = false,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                updateWindow.updatelog.Text = client.UpdateMessage;
                updateWindow.msg.Content = $"有新版本V{client.UpdateVersion}可以下载";
                updateWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                var traces = services.GetService<IWMWorkspaceTraceStore>();
                if (traces is not null)
                {
                    await traces.RecordLogAsync(new WMDiagnosticLogEvent(
                        DateTime.UtcNow,
                        WMDiagnosticLogLevel.Warning,
                        "Application.WindowsUpdate",
                        "windows-auto-update-check-failed",
                        ex.Message,
                        ex.GetType().FullName,
                        $"0x{ex.HResult:X8}",
                        StackTrace: ex.StackTrace));
                }
            }
        }
    }
}
