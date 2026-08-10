#if ANDROID
using Android.Content;
using Android.OS;
#endif
using Watermark.Shared.Models;

namespace Watermark.Andorid.Models
{
    public class UpgradeService : IUpgradeService
    {
        private const string UpdateDirectoryName = "updates";
        readonly HttpClient _client;
        public UpgradeService()
        {
            _client = new HttpClient();
        }
        public async Task<Dictionary<string, string>> CheckUpdatesAsync(string url)
        {
            var result = new Dictionary<string, string>();
            // 获取当前版本号
            var currentVersion = VersionTracking.CurrentVersion;
            var latestVersion = await _client.GetStringAsync(url);
            result.Add("CurrentVersion", currentVersion);
            result.Add("LatestVersion", latestVersion);
            return result;
        }

        public void InstallNewVersion()
        {
#if ANDROID
            var file = GetUpdateFilePath();
            if (!File.Exists(file) || new FileInfo(file).Length == 0)
                throw new FileNotFoundException("下载的安装包不存在。", file);

            var apkFile = new Java.IO.File(file);

            var intent = new Intent(Intent.ActionView);
            // 判断Android版本
            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
            {
                //给临时读取权限
                intent.SetFlags(ActivityFlags.GrantReadUriPermission);
                var authority = $"{AppInfo.PackageName}.updateFileProvider";
                var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(Android.App.Application.Context, authority, apkFile);
                // 设置显式 MIME 数据类型
                intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            }
            else
            {
                intent.SetDataAndType(Android.Net.Uri.FromFile(new Java.IO.File(file)), "application/vnd.android.package-archive");
            }
            //指定以新任务的方式启动Activity
            intent.AddFlags(ActivityFlags.NewTask);

            //激活一个新的Activity
            Android.App.Application.Context.StartActivity(intent);
#elif MACCATALYST
            // On macOS, updates are handled via browser download
#endif
        }

        public async Task DownloadFileAsync(string url, Action<long, long> action)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri)
                || (!string.Equals(downloadUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("更新下载地址无效。");

            var file = GetUpdateFilePath();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, downloadUri);
                using var response = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                var allLength = response.Content.Headers.ContentLength ?? 0;
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(
                    file,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    useAsync: true);
                var buffer = new byte[64 * 1024];
                long readLength = 0;
                int length;
                while ((length = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) != 0)
                {
                    readLength += length;
                    await fileStream.WriteAsync(buffer.AsMemory(0, length));
                    action(readLength, allLength);
                }
                await fileStream.FlushAsync();
                if (readLength == 0)
                    throw new InvalidDataException("更新服务器返回了空安装包。");
                action(readLength, allLength > 0 ? allLength : readLength);
            }
            catch
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch
                {
                    // Preserve the download failure when cleanup is not possible.
                }
                throw;
            }
        }

        private static string GetUpdateFilePath()
        {
            var directory = Path.Combine(FileSystem.AppDataDirectory, UpdateDirectoryName);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, Global.APK);
        }
    }
}
