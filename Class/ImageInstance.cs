namespace JointWatermark.Class
{
    public class ImageInstance
    {
        public ImageInstance(string url, string name)
        {
            Url = url;
            Name = name;
        }
        public string Url { get; set; }
        public string Name { get; set; }
    }
}
