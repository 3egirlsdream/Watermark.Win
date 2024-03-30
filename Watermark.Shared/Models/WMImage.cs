namespace Watermark.Win.Models
{
    public class WMImage
    {
        public WMImage()
        {
            EnableRadius = false;
            EnableShadow = false;
            ShadowRange = 30;
            ShadowColor = "#FF808080";
            CornerRadius = 15;
            EnableGaussianBlur = false;
            GaussianDeep = 50;
            Show = true;
        }
        public bool EnableShadow { get; set; }
        /// <summary>
        /// 阴影深度
        /// </summary>
        public int ShadowRange {  get; set; }
        public string ShadowColor { get; set; }
        public bool EnableRadius { get; set; }
        public int CornerRadius { get; set; }

        public bool EnableGaussianBlur { get; set; }
        public int GaussianDeep { get; set; }
        /// <summary>
        /// 是否显示图片
        /// </summary>
        public bool Show {  get; set; }
    }

}
