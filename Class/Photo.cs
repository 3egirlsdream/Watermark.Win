namespace JointWatermark.Class
{
    public class Photo
    {
        public Photo() { }
        public Photo(string path , bool isCloud, bool isLogo) 
        { 
            Path = path;
            IsCloud = isCloud;
            IsLogo = isLogo;
        }
        public string Path { get; set; }
        public bool IsCloud { get; set; }
        public bool IsLogo { get; set; }
    }
}
