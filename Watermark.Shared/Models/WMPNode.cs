namespace Watermark.Win.Models
{
    public class WMPNode
    {
        public WMPNode(int seq, string id) 
        { 
            SEQ = seq;
            PID = id;
        }
        public int SEQ { get; set; }
        public string PID {  get; set; }
    }

}
