using SkiaSharp;
using static System.Net.Mime.MediaTypeNames;
using System.Text;
using Watermark.Win.Models;

namespace Watermark.Shared.Models
{
    public static class ClientInstance
    {

        public static Action<WMCanvas, WMLogo, Dictionary<string, string>> SelectImageAction = (canvas, mLogo, ImagesBase64) =>
        {
        };



        public static Action<List<string>> InitLocalFontsAction = (Fonts) =>
        {
           
        };


        public static Action<List<string>> ImportLocalFontAction = (Fonts) =>
        {
            
        };

        public static Action<WMCanvas, WMText, string> SelectLocalFontAction = (CurrentCanvas, mText, fontName) =>
        {
            
        };

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

        public static string Key()
        {
            var result = (UUID().Replace("-", "") + "CATLNMSL");
            string result3 = result.Replace("-", "");
            return result3;
        }

        public static void OpenSetting()
        {
            
        }
    }
}