namespace Watermark
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            MainPage = new MainPage();
            AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
            {
                try
                {
                    var text = error.ExceptionObject.ToString() ?? "";
                }
                catch { }
            };
        }
    }
}
