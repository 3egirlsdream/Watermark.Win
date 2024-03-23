using SkiaSharp;

namespace Watermark.Win.Models
{
    public class WMZipedTemplate : IDisposable
    {
        private bool disposedValue;

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
        public string Name { get; set; }
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
        /// <summary>
        /// 市场里搜索
        /// </summary>
        public bool State {  get; set; } = true;
        /// <summary>
        /// 上架 下架
        /// </summary>
        public bool Visible { get; set; }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)
                    foreach (var item in Images)
                    {
                        item.Value?.Dispose();
                    }
                    Bitmap?.Dispose();
                    Fonts = null;
                }

                // TODO: 释放未托管的资源(未托管的对象)并重写终结器
                // TODO: 将大型字段设置为 null
                disposedValue=true;
            }
        }

        // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        // ~WMZipedTemplate()
        // {
        //     // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
