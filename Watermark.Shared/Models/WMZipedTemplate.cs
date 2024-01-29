using SkiaSharp;

namespace Watermark.Win.Models
{
    public class WMZipedTemplate
    {
        public WMZipedTemplate()
        {
            Images = new Dictionary<string, SKBitmap>();
            Fonts = new Dictionary<string, Stream>();
        }
        public WMCanvas WMCanvas { get; set; }
        public Dictionary<string, SKBitmap> Images { get; set; }
        public Dictionary<string, Stream> Fonts { get; set; }
        public SKBitmap Bitmap { get; set; }
        public string Src { get; set; }
        public string Desc { get; set; }
        public string WatermarkId { get; set; }
        public int DownloadTimes { get; set; }
        public int Coins { get; set; }
    }
}
