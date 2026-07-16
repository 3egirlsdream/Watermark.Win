namespace Watermark.Andorid
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            MainPage = new MainPage();
		}

		protected override Window CreateWindow(IActivationState? activationState)
		{
			var window = base.CreateWindow(activationState);
#if MACCATALYST
				window.Created += (s, e) =>
				{
					WindowHelper.ConfigureTitleBar();
				};
#endif
			return window;
		}

		public static object HttpClientHandler;

    }
}
