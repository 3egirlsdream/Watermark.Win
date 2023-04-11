namespace JointWatermark.Class
{
    public class ImageShadow
    {
        public ImageShadow() { }
        public ImageShadow(bool enabled, int width) 
        { 
            Enabled = enabled;
            Width = width;
        }
        public bool Enabled { get; set; }
        public int Width { get; set; } = 200;
    }
}
