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
        }
        public bool EnableShadow { get; set; }
        public int ShadowRange {  get; set; }
        public string ShadowColor { get; set; }
        public bool EnableRadius { get; set; }
        public int CornerRadius { get; set; }

        public bool EnableGaussianBlur { get; set; }
        public int GaussianDeep { get; set; }
    }

}
