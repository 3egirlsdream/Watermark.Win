using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Watermark.Win.Models;
using Watermark.Win.Views;
using System.IO;
using Watermark.Shared.Models;
using Watermark.Razor.Workspace;

namespace Watermark.Win
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            try
            {
				Global.DeviceType = Watermark.Shared.Enums.DeviceType.Win;
				IocHelper.GetIoc().AddSingleton<WatermarkHelper>();
                IocHelper.GetIoc().AddSingleton<IWMWatermarkHelper>(provider => provider.GetRequiredService<WatermarkHelper>());
				IocHelper.GetIoc().AddSingleton<APIHelper>();
                IocHelper.GetIoc().AddSingleton<IWindowService, WindowService>();
                IocHelper.GetIoc().AddScoped<MainInterop>();
                IocHelper.GetIoc().AddSingleton<IClientInstance, ClientInstance>();
                IocHelper.GetIoc().AddSingleton<IWMPhotoMetadataReader, WMMetadataExtractorReader>();
                IocHelper.GetIoc().AddSingleton<IWMSourceStager, WMLocalSourceStager>();
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
                Loaded+=MainWindow_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CheckUpdate();
        }

        protected override void OnClosed(EventArgs e)
        {
            var path = Global.AppPath.ThumbnailFolder;
            if(Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        public void CheckUpdate()
        {
            var day = DateTime.Now.DayOfYear;
            if (day % 3 == 0)
            {
#pragma warning disable CS8602 // 解引用可能出现空引用。
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
#pragma warning restore CS8602 // 解引用可能出现空引用。
                var action = new Action<string, string>((t, m) =>
                {
                    var win = new UpdateWin();
                    win.updatelog.Text = m;
                    win.msg.Content = t;
                    win.ShowInTaskbar = false;
                    win.Owner = this;
                    win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    win.ShowDialog();
                });

                CheckUpdate(v, action);
            }
        }

        public async void CheckUpdate(string nowv, Action<string, string> action)
        {
            var version = await Connections.HttpGetAsync<WMClientVersion>(APIHelper.HOST + "/api/CloudSync/GetVersion?Client=WatermarkV3", Encoding.Default);
            if (version != null && version.success && version.data != null && version.data.VERSION != null)
            {
                var v1 = new Version(nowv);
                var v2 = new Version(version.data.VERSION);
                if (v2 > v1)
                    action.Invoke($"有新版本V{version.data.VERSION}可以下载", version.data.MEMO);
            }
        }
    }
}
