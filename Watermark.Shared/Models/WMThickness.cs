namespace Watermark.Win.Models
{
    public class WMThickness
    {
        public WMThickness() { }
        public WMThickness(double v)
        {
            Bottom = Left = Top = Right = v;
        }
        public WMThickness(double left, double top, double right, double bottom)
        {
            Bottom = bottom;
            Left = left;
            Right = right;
            Top = top;
        }

        public double Bottom { get; set; }
        public double Left { get; set; }
        public double Right { get; set; }
        public double Top { get; set; }

    }

}
