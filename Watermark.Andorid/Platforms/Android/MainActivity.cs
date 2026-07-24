using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Views;
using AndroidX.Activity;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using Watermark.Razor.Workspace;
using Color = Android.Graphics.Color;

namespace Watermark.Andorid
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
	public class MainActivity : MauiAppCompatActivity
	{
		private const int CreateDocumentRequestCode = 4103;
		private const int PickImagesRequestCode = 4104;
		private readonly object documentRequestGate = new();
		private readonly object photoPickerRequestGate = new();
		private TaskCompletionSource<Android.Net.Uri?>? documentRequest;
		private TaskCompletionSource<IReadOnlyList<Android.Net.Uri>>? photoPickerRequest;
		protected override void OnCreate(Bundle? savedInstanceState)
		{
#if ANDROID
			Instance = this;
			//设置状态栏，导航样颜色为透明
			var startupBackground = Color.ParseColor("#FAFAFA");
			Window?.SetStatusBarColor(startupBackground);
			Window?.SetNavigationBarColor(startupBackground);
#pragma warning disable CA1422
			if (Window?.DecorView is not null)
				Window.DecorView.SystemUiFlags = SystemUiFlags.LightStatusBar;
#pragma warning restore CA1422
            Action<string> action = (hex) =>
            {
                var color = Color.ParseColor(hex);
                Window?.SetStatusBarColor(color);
                if (Window?.DecorView is not null)
                {
                    var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255d;
#pragma warning disable CA1422
                    Window.DecorView.SystemUiFlags = luminance > .55
                        ? SystemUiFlags.LightStatusBar
                        : 0;
#pragma warning restore CA1422
                }
            };
            SetColor = action;
#endif  
			base.OnCreate(savedInstanceState);
			OnBackPressedDispatcher.AddCallback(this, new WorkspaceBackCallback(this));
		}

		protected override void OnResume()
		{
			base.OnResume();

			GetMainPage()?.ResumeAndroidWebView();
		}

		public override void OnWindowFocusChanged(bool hasFocus)
		{
			base.OnWindowFocusChanged(hasFocus);
			if (hasFocus) GetMainPage()?.RedrawAndroidWebView();
		}

		private static MainPage? GetMainPage() =>
			Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page as MainPage;
		public static MainActivity? Instance { get; private set; }

        public static Action<string>? SetColor;

		public Task<Android.Net.Uri?> CreateDocumentAsync(
			string suggestedFileName,
			string mimeType,
			CancellationToken cancellationToken = default)
		{
			var completion = new TaskCompletionSource<Android.Net.Uri?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			lock (documentRequestGate)
			{
				if (documentRequest is not null)
					throw new InvalidOperationException("已有系统文件保存器正在等待用户操作。");
				documentRequest = completion;
			}

			RunOnUiThread(() =>
			{
				try
				{
					var intent = new Intent(Intent.ActionCreateDocument);
					intent.AddCategory(Intent.CategoryOpenable);
					intent.SetType(mimeType);
					intent.PutExtra(Intent.ExtraTitle, Path.GetFileName(suggestedFileName));
					intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
					StartActivityForResult(intent, CreateDocumentRequestCode);
				}
				catch (Exception ex)
				{
					CompleteDocumentRequest(completion, null, ex);
				}
			});

			return AwaitDocumentRequestAsync(completion, cancellationToken);
		}

		public Task<IReadOnlyList<Android.Net.Uri>> PickImagesAsync(
			CancellationToken cancellationToken = default)
		{
			var completion = new TaskCompletionSource<IReadOnlyList<Android.Net.Uri>>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			lock (photoPickerRequestGate)
			{
				if (photoPickerRequest is not null)
					throw new InvalidOperationException("已有系统照片选择器正在等待用户操作。");
				photoPickerRequest = completion;
			}

			RunOnUiThread(() =>
			{
				try
				{
					StartActivityForResult(CreatePhotoPickerIntent(), PickImagesRequestCode);
				}
				catch (Exception ex)
				{
					CompletePhotoPickerRequest(completion, [], ex);
				}
			});

			return AwaitPhotoPickerRequestAsync(completion, cancellationToken);
		}

		private Intent CreatePhotoPickerIntent()
		{
			var packageManager = PackageManager;
			// Android 13+ uses the privacy-preserving system Photo Picker. It shows
			// albums/photos directly and does not require storage permissions.
			if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
			{
				var photoPicker = new Intent("android.provider.action.PICK_IMAGES");
				photoPicker.SetType("image/*");
				photoPicker.PutExtra("android.provider.extra.PICK_IMAGES_MAX", 100);
				photoPicker.AddFlags(ActivityFlags.GrantReadUriPermission);
				if (packageManager is not null && photoPicker.ResolveActivity(packageManager) is not null)
					return photoPicker;
			}

			// API 24-32 opens an installed gallery/media application. ACTION_OPEN_DOCUMENT
			// remains the last-resort fallback only when no gallery can handle ACTION_PICK.
			var galleryPicker = new Intent(Intent.ActionPick);
			galleryPicker.SetDataAndType(MediaStore.Images.Media.ExternalContentUri, "image/*");
			galleryPicker.PutExtra(Intent.ExtraAllowMultiple, true);
			galleryPicker.AddFlags(ActivityFlags.GrantReadUriPermission);
			if (packageManager is not null && galleryPicker.ResolveActivity(packageManager) is not null)
				return galleryPicker;

			var documentPicker = new Intent(Intent.ActionOpenDocument);
			documentPicker.AddCategory(Intent.CategoryOpenable);
			documentPicker.SetType("image/*");
			documentPicker.PutExtra(Intent.ExtraAllowMultiple, true);
			documentPicker.AddFlags(ActivityFlags.GrantReadUriPermission);
			return documentPicker;
		}

		protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
		{
			base.OnActivityResult(requestCode, resultCode, data);
			if (requestCode == CreateDocumentRequestCode)
			{
				TaskCompletionSource<Android.Net.Uri?>? completion;
				lock (documentRequestGate)
				{
					completion = documentRequest;
					documentRequest = null;
				}
				completion?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
				return;
			}

			if (requestCode != PickImagesRequestCode) return;
			TaskCompletionSource<IReadOnlyList<Android.Net.Uri>>? photoCompletion;
			lock (photoPickerRequestGate)
			{
				photoCompletion = photoPickerRequest;
				photoPickerRequest = null;
			}
			photoCompletion?.TrySetResult(
				resultCode == Result.Ok ? ReadPickedImageUris(data) : []);
		}

		private static IReadOnlyList<Android.Net.Uri> ReadPickedImageUris(Intent? data)
		{
			var result = new List<Android.Net.Uri>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var clipData = data?.ClipData;
			if (clipData is not null)
			{
				for (var index = 0; index < clipData.ItemCount; index++)
				{
					var uri = clipData.GetItemAt(index)?.Uri;
					var key = uri?.ToString();
					if (uri is not null && !string.IsNullOrWhiteSpace(key) && seen.Add(key)) result.Add(uri);
				}
			}
			var single = data?.Data;
			var singleKey = single?.ToString();
			if (single is not null && !string.IsNullOrWhiteSpace(singleKey) && seen.Add(singleKey))
				result.Add(single);
			return result;
		}

		private async Task<Android.Net.Uri?> AwaitDocumentRequestAsync(
			TaskCompletionSource<Android.Net.Uri?> completion,
			CancellationToken cancellationToken)
		{
			using var registration = cancellationToken.Register(() =>
				completion.TrySetCanceled(cancellationToken));
			return await completion.Task.ConfigureAwait(false);
		}

		private async Task<IReadOnlyList<Android.Net.Uri>> AwaitPhotoPickerRequestAsync(
			TaskCompletionSource<IReadOnlyList<Android.Net.Uri>> completion,
			CancellationToken cancellationToken)
		{
			using var registration = cancellationToken.Register(() =>
				completion.TrySetCanceled(cancellationToken));
			return await completion.Task.ConfigureAwait(false);
		}

		private void CompleteDocumentRequest(
			TaskCompletionSource<Android.Net.Uri?> completion,
			Android.Net.Uri? uri,
			Exception? error = null)
		{
			lock (documentRequestGate)
			{
				if (ReferenceEquals(documentRequest, completion)) documentRequest = null;
			}
			if (error is null) completion.TrySetResult(uri);
			else completion.TrySetException(error);
		}

		private void CompletePhotoPickerRequest(
			TaskCompletionSource<IReadOnlyList<Android.Net.Uri>> completion,
			IReadOnlyList<Android.Net.Uri> uris,
			Exception? error = null)
		{
			lock (photoPickerRequestGate)
			{
				if (ReferenceEquals(photoPickerRequest, completion)) photoPickerRequest = null;
			}
			if (error is null) completion.TrySetResult(uris);
			else completion.TrySetException(error);
		}

        private sealed class WorkspaceBackCallback(MainActivity activity)
            : OnBackPressedCallback(true)
        {
            public override void HandleOnBackPressed()
            {
                var dispatcher = IPlatformApplication.Current?.Services
                    .GetService<IWMSystemBackDispatcher>();
                if (dispatcher?.TryDispatch() == true) return;

                // Returning from the root page must leave the task rather than
                // finish the MAUI Activity. Finishing it leaves some Android
                // WebView implementations with a detached compositor surface
                // when the user restores the task from Recents.
                activity.MoveTaskToBack(true);
            }
        }

    }

    public static class SavePictureService
    {
        public static bool SavePicture(byte[] arr, string imageName)
        {
            var activity = MainActivity.Instance;
            if (activity is null || arr.Length == 0) return false;
            if (IsAtLeastAndroid29)
                return SaveModern(activity, arr, imageName);
            if (ContextCompat.CheckSelfPermission(activity, Android.Manifest.Permission.WriteExternalStorage)
                != Android.Content.PM.Permission.Granted)
            {
                ActivityCompat.RequestPermissions(
                    activity,
                    [Android.Manifest.Permission.WriteExternalStorage],
                    4102);
                return false;
            }
            return SaveLegacy(activity, arr, imageName);
        }

        [SupportedOSPlatform("android29.0")]
        private static bool SaveModern(MainActivity activity, byte[] arr, string imageName)
        {
            var contentValues = new ContentValues();
            contentValues.Put(MediaStore.IMediaColumns.DisplayName, Path.GetFileName(imageName));
            contentValues.Put(MediaStore.Files.IFileColumns.MimeType, "image/jpeg");
            contentValues.Put(MediaStore.IMediaColumns.RelativePath, "Pictures/Litograph");
            contentValues.Put(MediaStore.IMediaColumns.IsPending, 1);
            try
            {
                var resolver = activity.ContentResolver
                               ?? throw new InvalidOperationException("Android ContentResolver 不可用。");
                var collection = MediaStore.Images.Media.ExternalContentUri
                                 ?? throw new InvalidOperationException("系统相册不可用。");
                var uri = resolver.Insert(collection, contentValues)
                          ?? throw new IOException("无法创建系统相册文件。");
                try
                {
                    using var output = resolver.OpenOutputStream(uri)
                                       ?? throw new IOException("无法写入系统相册文件。");
                    output.Write(arr, 0, arr.Length);
                    output.Flush();
                    var completed = new ContentValues();
                    completed.Put(MediaStore.IMediaColumns.IsPending, 0);
                    resolver.Update(uri, completed, null, null);
                }
                catch
                {
                    resolver.Delete(uri, null, null);
                    throw;
                }
            }
            catch (System.Exception ex)
            {
                Console.Write(ex.ToString());
                return false;
            }
            return true;
        }

        [SupportedOSPlatformGuard("android29.0")]
        private static bool IsAtLeastAndroid29 => Build.VERSION.SdkInt >= BuildVersionCodes.Q;

        private static bool SaveLegacy(MainActivity activity, byte[] arr, string imageName)
        {
            try
            {
#pragma warning disable CA1422
                var pictures = Android.OS.Environment.GetExternalStoragePublicDirectory(
                    Android.OS.Environment.DirectoryPictures)?.AbsolutePath;
#pragma warning restore CA1422
                if (string.IsNullOrWhiteSpace(pictures)) return false;
                var directory = Path.Combine(pictures, "Litograph");
                Directory.CreateDirectory(directory);
                var target = WMLocalExportSink.UniquePath(directory, Path.GetFileName(imageName));
                File.WriteAllBytes(target, arr);
                Android.Media.MediaScannerConnection.ScanFile(
                    activity, [target], ["image/jpeg"], null);
                return true;
            }
            catch (Exception ex)
            {
                Console.Write(ex);
                return false;
            }
        }
    }
}
