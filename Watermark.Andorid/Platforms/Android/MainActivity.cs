using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
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
			Instance = this;
			//设置状态栏，导航样颜色为透明
			Window.SetStatusBarColor(Color.White);
			Window.SetNavigationBarColor(Color.White);
			Window.DecorView.SystemUiVisibility = (StatusBarVisibility)SystemUiFlags.LightStatusBar;
            Action<string> action = (hex) =>
            {
                Window.SetStatusBarColor(Color.ParseColor(hex));
            };
            SetColor = action;
#endif  
			base.OnCreate(savedInstanceState);
		}
		public static MainActivity Instance { get; private set; }

        public static Action<string> SetColor;

    }

    public static class SavePictureService
    {
        public static bool SavePicture(byte[] arr, string imageName)
        {
            var contentValues = new ContentValues();
            contentValues.Put(MediaStore.IMediaColumns.DisplayName, imageName);
            contentValues.Put(MediaStore.Files.IFileColumns.MimeType, "image/jpeg");
            contentValues.Put(MediaStore.IMediaColumns.RelativePath, "Pictures/DaVinciFrameMaster");
            try
            {
                var uri = MainActivity.Instance.ContentResolver.Insert(MediaStore.Images.Media.ExternalContentUri, contentValues);
                var output = MainActivity.Instance.ContentResolver.OpenOutputStream(uri);
                output.Write(arr, 0, arr.Length);
                output.Flush();
                output.Close();
            }
            catch (System.Exception ex)
            {
                Console.Write(ex.ToString());
                return false;
            }
            contentValues.Put(MediaStore.IMediaColumns.IsPending, 1);
            return true;
        }
    }
}
