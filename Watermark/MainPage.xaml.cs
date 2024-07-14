using SkiaSharp;

namespace Watermark
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            try
            {
                var file = await MediaPicker.Default.PickPhotoAsync();
                if (file != null)
                {
                    var data = SKData.Create(file.FullPath);
                    var dt = data.ToArray();
                    Platforms.iOS.SavePictureService.SavePicture(dt);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
