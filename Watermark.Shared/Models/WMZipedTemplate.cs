using SkiaSharp;

namespace Watermark.Win.Models
{
    public class WMZipedTemplate
    {
        public WMZipedTemplate()
        {
            Images = new Dictionary<string, SKBitmap>();
            Fonts = new Dictionary<string, byte[]>();
        }
        public WMCanvas WMCanvas { get; set; }
        public Dictionary<string, SKBitmap> Images { get; set; }
        public Dictionary<string, byte[]> Fonts { get; set; }
        public SKBitmap Bitmap { get; set; }
        public string Src { get; set; }
        public string Desc { get; set; }
        public string WatermarkId { get; set; }
        public string UserId { get; set; }
        public string UserDisplayName { get; set; }
        public int DownloadTimes { get; set; }
        public int Coins { get; set; }
        /// <summary>
        /// 官方精选
        /// </summary>
        public bool Recommend { get; set; }
        public DateTime DateTimeCreated { get; set; }
    }
}
