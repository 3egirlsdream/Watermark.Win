using Microsoft.AspNetCore.Components.WebView;

namespace Watermark.Andorid
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
		}

		private void blazorWebView_BlazorWebViewInitialized(object sender, Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs e)
        {
#if ANDROID
            //e.WebView.Settings.MixedContentMode = Android.Webkit.MixedContentHandling.AlwaysAllow;
			e.WebView.VerticalScrollBarEnabled = false;
            e.WebView.ScrollBarSize = 0;
#endif
		}
    }
}
