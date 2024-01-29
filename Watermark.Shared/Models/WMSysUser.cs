namespace Watermark.Win.Models
{
    public class WMSysUser
    {
        public string? ID { get; set; }
        public required string USER_NAME { get; set; }
        public required string DISPLAY_NAME { get; set; }
        public required string PASSWORD { get; set; }
        public required string PK_ID { get; set; }
    }
}
