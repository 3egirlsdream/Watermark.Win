﻿namespace Watermark.Win.Models
{
    public class WMLoginChildModel
    {
        public string ID { get; set; }
        public string IMG { get; set; }
        public string DISPLAY_NAME { get; set; }
        public string USER_NAME { get; set; }
        public DateTime? EXPIRE_DATE { get; set; }
        public bool IsVIP
        {
            get => EXPIRE_DATE > DateTime.Now;
        }
    }
}
