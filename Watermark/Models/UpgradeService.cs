#if ANDROID
using Android.Content;
using Android.OS;
#endif
using Watermark.Shared.Models;

namespace Watermark.Models
{
    public class UpgradeService : IUpgradeService
    {
        readonly HttpClient _client;
        public UpgradeService()
        {
            _client = new HttpClient();
        }
        public async Task<Dictionary<string, string>> CheckUpdatesAsync(string url)
        {
            return [];
        }

        public void InstallNewVersion()
        {
        }

        public Task DownloadFileAsync(string url, Action<long, long> action)
        {
            return Task.CompletedTask;
        }
    }
}
