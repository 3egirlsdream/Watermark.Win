namespace Watermark.Win.Models
{
    public class WMLogo : IWMControl
    {
        public WMLogo()
        {
            ID = Guid.NewGuid().ToString("N").ToUpper();
        }
        public string Path { get; set; }
        public bool White2Transparent { get; set; }
        public bool AutoSetLogo {  get; set; }
    }

}
