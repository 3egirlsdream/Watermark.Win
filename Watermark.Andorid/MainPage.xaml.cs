using Microsoft.AspNetCore.Components.WebView;
#if IOS
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
#endif

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
#if IOS
            // Let the WebView own both safe areas. The shared mobile layout uses
            // CSS env(safe-area-inset-*) so its bottom bar can paint continuously
            // behind the Home Indicator instead of leaving a native white strip.
            On<Microsoft.Maui.Controls.PlatformConfiguration.iOS>()
                .SetUseSafeArea(false);
#endif
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
			RedrawAndroidWebView();
#endif
		}

		/// <summary>
		/// Requests a new frame after Android has attached the activity window.
		/// This is also called when the activity regains focus, which happens after
		/// the WebView's compositor surface is available again.
		/// </summary>
		public void RedrawAndroidWebView()
		{
#if ANDROID
			var webView = androidWebView;
			if (webView is null) return;

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
#elif IOS
            // The page deliberately extends edge-to-edge. Prevent WKWebView from
            // adding a second native content inset; the shared CSS consumes the
            // reported safe-area values for headers and the bottom navigation.
            e.WebView.ScrollView.ContentInsetAdjustmentBehavior =
                UIKit.UIScrollViewContentInsetAdjustmentBehavior.Never;
            e.WebView.ScrollView.ContentInset = UIKit.UIEdgeInsets.Zero;
            e.WebView.ScrollView.ScrollIndicatorInsets = UIKit.UIEdgeInsets.Zero;
#elif MACCATALYST
            // Configure WKWebView for macOS
            e.WebView.Configuration.Preferences.SetValueForKey(
                Foundation.NSObject.FromObject(true), 
                new Foundation.NSString("developerExtrasEnabled"));
#endif
		}
    }
}
