#if ANDROID
using Android.Content;
using Android.OS;
#endif
using Watermark.Shared.Models;

namespace Watermark.Models
{
    public interface IUpgradeService
    {
        /// <summary>
        /// 检查更新
        /// </summary>
        /// <param name="url">
        /// 检查URL
        /// </param>
        /// <returns></returns>
        Task<Dictionary<string, string>> CheckUpdatesAsync(string url);

        /// <summary>
        /// 下载安装文件
        /// </summary>
        /// <param name="url">
        /// 下载URL
        /// </param>
        /// <param name="action">
        /// 进度条处理方法
        /// </param>
        /// <returns></returns>
        Task DownloadFileAsync(string url, Action<long, long> action);

        /// <summary>
        /// 安装APK的方法
        /// </summary>
        void InstallNewVersion();
    }
    public class UpgradeService : IUpgradeService
    {
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
            var file = $"{FileSystem.AppDataDirectory}/{Global.APK}";

            var apkFile = new Java.IO.File(file);

            var intent = new Intent(Intent.ActionView);
            // 判断Android版本
            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
            {
                //给临时读取权限
                intent.SetFlags(ActivityFlags.GrantReadUriPermission);
                var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(Android.App.Application.Context, "com.masa.mauidemo.fileprovider", apkFile);
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
#endif
        }

        public async Task DownloadFileAsync(string url, Action<long, long> action)
        {
            var req = new HttpRequestMessage(new HttpMethod("GET"), url);
            var response = _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).Result;
            var allLength = response.Content.Headers.ContentLength;
            var stream = await response.Content.ReadAsStreamAsync();

            var file = $"{FileSystem.AppDataDirectory}/{Global.APK}";
            await using var fileStream = new FileStream(file, FileMode.Create);
            await using (stream)
            {
                var buffer = new byte[10240];
                var readLength = 0;
                int length;
                while ((length = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    readLength += length;
                    action(readLength, allLength!.Value);
                    // 写入到文件
                    fileStream.Write(buffer, 0, length);
                }
            }
        }
    }
}
