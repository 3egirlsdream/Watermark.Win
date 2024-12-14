using SkiaSharp;
using static System.Net.Mime.MediaTypeNames;
using System.Text;
using MudBlazor;
using Masa.Blazor;
using Microsoft.JSInterop;
using Masa.Blazor.Presets;

namespace Watermark.Shared.Models
{
    public class ClientInstance : IClientInstance
    {


        public Action<List<string>> InitLocalFontsAction = (Fonts) =>
        {
           
        };


        public Action<List<string>> ImportLocalFontAction = (Fonts) =>
        {
            
        };

        public string UpdateMessage { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string UpdateVersion { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string LinkPath { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public static void SelectDefaultImage(string id, Dictionary<string, string> dic)
        {
            try
            {
                
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        static void WriteThumbnailImage(SKBitmap source, string target)
        {
           
        }

        private static string UUID()
        {
            
            return "";
        }

        public string Key()
        {
            var result = (UUID().Replace("-", "") + "CATLNMSL");
            string result3 = result.Replace("-", "");
            return result3;
        }

        public static void OpenSetting()
        {
            
        }

        public static void ShowMsg(ISnackbar snackbar, string message, Severity severity)
        {
            snackbar.Configuration.PositionClass = Defaults.Classes.Position.TopCenter;
            snackbar?.Add(message, severity, config =>
            {
                config.ShowCloseIcon = false;
            });
        }


        public void Haptic()
        {
            throw new NotImplementedException();
        }

        public Task DownloadTemplate(string watermarkId, ViewParameter parameter, IPopupService PopupService, List<WMZipedTemplate> ZipedTemplates, IWMWatermarkHelper helper, IJSRuntime JSRuntime, Dictionary<string, int> Versions, PageStackNavController NavController, FailedBox failedBox)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<string>> PickMultipleAsync()
        {
            throw new NotImplementedException();
        }

        public Task<string> PickAsync()
        {
            throw new NotImplementedException();
        }

        public Version GetVersion()
        {
            throw new NotImplementedException();
        }

        public Task<bool> CheckUpdate(string platform = "")
        {
            throw new NotImplementedException();
        }

        public Task SetTextAsync(string uri)
        {
            throw new NotImplementedException();
        }

        public Task<API<string>> AliPays(decimal cost, string tradeName)
        {
            throw new NotImplementedException();
        }

        public Task ReLogin()
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsOutOfDate(string client = "Watermark_A")
        {
            throw new NotImplementedException();
        }

        public bool Save(byte[] b64, string fn)
        {
            throw new NotImplementedException();
        }

        public Task<string> OpenFolder()
        {
            throw new NotImplementedException();
        }

        public void SetColor(string color = "#F5F5F5")
        {
            throw new NotImplementedException();
        }

        public Task<WMDesignFunc> GetWMDesignFunc(string canvasId)
        {
            throw new NotImplementedException();
        }

        public Task Update(Action<long, long> DownloadProgressChanged)
        {
            throw new NotImplementedException();
        }
    }
}