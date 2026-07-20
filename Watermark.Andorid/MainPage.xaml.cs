using Microsoft.AspNetCore.Components.WebView;

namespace Watermark.Andorid
{
    public partial class MainPage : ContentPage
    {
#if ANDROID
        private Android.Webkit.WebView? androidWebView;
#endif

        public MainPage()
        {
            InitializeComponent();
		}

		/// <summary>
		/// Restores the native WebView after the Android activity returns from the
		/// background. Some Android WebView implementations lose their compositor
		/// surface during that transition while the Blazor circuit remains alive.
		/// This deliberately redraws the existing page instead of reloading it, so
		/// the current workspace and its in-memory edit state are retained.
		/// </summary>
		public void ResumeAndroidWebView()
		{
#if ANDROID
			var webView = androidWebView;
			if (webView is null) return;

			webView.OnResume();
			webView.ResumeTimers();
			webView.RequestLayout();
			webView.Invalidate();
			webView.PostInvalidateOnAnimation();
#endif
		}

		private void blazorWebView_BlazorWebViewInitialized(object sender, Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs e)
        {
#if ANDROID
            //e.WebView.Settings.MixedContentMode = Android.Webkit.MixedContentHandling.AlwaysAllow;
			androidWebView = e.WebView;
			e.WebView.VerticalScrollBarEnabled = false;
            e.WebView.ScrollBarSize = 0;
#elif MACCATALYST
            // Configure WKWebView for macOS
            e.WebView.Configuration.Preferences.SetValueForKey(
                Foundation.NSObject.FromObject(true), 
                new Foundation.NSString("developerExtrasEnabled"));
#endif
		}
    }
}
