using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Color = Android.Graphics.Color;

namespace Watermark.Andorid
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
		protected override void OnCreate(Bundle savedInstanceState)
		{
#if ANDROID
			//设置状态栏，导航样颜色为透明
			Window.SetStatusBarColor(Color.Transparent);
			Window.SetNavigationBarColor(Color.Transparent);
			Window.DecorView.SystemUiVisibility = (StatusBarVisibility)SystemUiFlags.LightStatusBar;
#endif
			base.OnCreate(savedInstanceState);
		}
	}
}
