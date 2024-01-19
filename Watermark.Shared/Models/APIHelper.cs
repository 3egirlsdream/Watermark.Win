using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Qiniu.Http;
using Qiniu.Storage;
using SkiaSharp;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Watermark.Win.Models
{
    public class APIHelper
    {
        public static string HOST = "http://thankful.top:4396";
        public APIHelper()
        {
            _client = new HttpClient() { BaseAddress =  new Uri(HOST) };
        }

        HttpClient _client;

        public async Task<API<bool?>> UploadWatermark(string watermarkId, string name, int coins, string desc = "")
        {

            var result1 = await UploadToQiniu(watermarkId, name);
            if (result1.success)
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent($"{watermarkId}.zip"), "path");
                form.Add(new StringContent(watermarkId), "watermarkId");
                form.Add(new StringContent(coins.ToString()), "coins");
                form.Add(new StringContent(desc), "desc");
                form.Add(new StringContent(Global.CurrentUser.ID), "userId");
                using var response = await _client.PostAsync("/api/Watermark/Upload", form);
                var bt = await response.Content.ReadAsByteArrayAsync();
                var str = Encoding.UTF8.GetString(bt);
                var result = JsonConvert.DeserializeObject<API<bool?>>(str);
                if (response.IsSuccessStatusCode)
                {
                    return result;
                }
            }
            return new API<bool?>() { success = result1.success, message = result1.message };
        }

        public async Task<API<string>> UploadToQiniu(string watermarkId, string name)
        {
            var request = await Connections.HttpGetAsync<string>(HOST + $"/api/Qiniu/GetToken?key={watermarkId}.zip", Encoding.UTF8);
            if (!request.success) return request;
            var token = request.data ?? "";
            var path = Global.TemplatesFolder + watermarkId;
            if (Directory.Exists(path))
            {
                var configPath = path + Path.DirectorySeparatorChar + "config.json";
                if (File.Exists(configPath))
                {
                    var wc = Global.ReadConfigFromPath(configPath);
                    wc.Name = name;
                    File.Delete(configPath);
                    var json = Global.CanvasSerialize(wc);
                    System.IO.File.WriteAllText(configPath, json);
                }

                var targetPath = Global.TemplatesFolder + $"{watermarkId}.zip";
                if (File.Exists(targetPath)) File.Delete(targetPath);
                ZipFile.CreateFromDirectory(path, targetPath);

                Config config = new Config();
                // 设置上传区域
                config.Zone = Zone.ZONE_CN_South;
                // 设置 http 或者 https 上传
                config.UseHttps = true;
                config.UseCdnDomains = true;
                config.ChunkSize = ChunkUnit.U512K;
                // 表单上传
                FormUploader target = new FormUploader(config);
                HttpResult result = target.UploadFile(targetPath, $"{watermarkId}.zip", token, null);
                return new API<string> { success = result.Code == 200, message = new APISub { content = result.Text } } ;
            }
            return new API<string>() { success = false, message = new APISub() { content = "文件不存在" } };
        }

        public async Task<List<ZipedTemplate>> GetWatermarks(int start, int length, string desc = "countDesc")
        {
            try
            {
                using HttpResponseMessage response = await _client.GetAsync($"/api/Watermark/GetWatermarks?userId={Global.CurrentUser.ID}&start={start}&length={length}&type={desc}");
                response.EnsureSuccessStatusCode();
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseObject = JsonConvert.DeserializeObject<API<dynamic>>(responseContent);

                if (responseObject != null && responseObject.success)
                {
                    var total = responseObject.data.Total;

                    var data = (JArray)responseObject.data.Data;
                    if (data == null) return [];
                    List<ZipedTemplate> templates = new List<ZipedTemplate>();
                    List<Task> tasks = new List<Task>();
                    foreach (var item in data)
                    {
                        var t = new ZipedTemplate();
                        t.WatermarkId = item?["ID"]?.ToString();
                        t.Desc = item?["DESC"]?.ToString();
                        t.Coins = Convert.ToInt32(item?["COINS"]?.ToString() ?? "0");
                        t.DownloadTimes = Convert.ToInt32(item?["DOWNLOAD_TIMES"]?.ToString() ?? "0");
                        templates.Add(t);
                    }

                    return templates;
                }
                else
                {
                    return new List<ZipedTemplate>();
                }
            }
            catch (Exception ex)
            {
                return [];
            }
        }
        
        public async Task<ZipedTemplate> ExtractZip(string watermarkId)
        {
            using var client = new HttpClient();
            var stream = await client.GetStreamAsync($"http://cdn.thankful.top/{watermarkId}.zip");
            using MemoryStream ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);
            using ZipArchive archive = new ZipArchive(ms, ZipArchiveMode.Read);
            ZipedTemplate t = new ZipedTemplate();
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith("config.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var entryStream = entry.Open();
                    using StreamReader reader = new StreamReader(entryStream);
                    string content = reader.ReadToEnd();
                    t.WMCanvas = Global.ReadConfig(content);
                    Console.WriteLine($"Content of {entry.FullName}: {content}");
                }
                else if (entry.FullName.EndsWith("default.jpg", StringComparison.OrdinalIgnoreCase))
                {
                    using var entryStream = entry.Open();
                    SKBitmap sKBitmap = SKBitmap.Decode(entryStream);
                    t.Bitmap = sKBitmap;
                }
                else if (entry.FullName.EndsWith(".ttf") || entry.FullName.EndsWith(".otf"))
                {
                    using var entryStream = entry.Open();
                    t.Fonts[entry.FullName] = entryStream;
                }
                else
                {
                    using var entryStream = entry.Open();
                    SKBitmap sKBitmap = SKBitmap.Decode(entryStream);
                    t.Images[entry.FullName] = sKBitmap;
                }
            }
            return t;
        }


        public async Task<API<SysUser>> Register(SysUser user)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(HOST);
                var bytes = MD5.HashData(Encoding.UTF8.GetBytes(user.PASSWORD));
                StringBuilder sb = new StringBuilder();
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                var password = sb.ToString();
                
                var formContent = new {
                    username = user.USER_NAME,
                    displayname = user.DISPLAY_NAME,
                    password = password,
                    pkid = user.PK_ID
                };

                var response = await client.PostAsJsonAsync("/api/Watermark/SignUp", formContent);
                var bt = await response.Content.ReadAsByteArrayAsync();
                var str = Encoding.UTF8.GetString(bt);
                var result = JsonConvert.DeserializeObject<API<SysUser>>(str);
                if (response.IsSuccessStatusCode)
                {
                    return result;
                }
                else return new API<SysUser>() { success = false };
            }
        }
    
        public async Task<API<LoginModel>> LoginIn(string user, string password, bool isMD5 = false)
        {
            try
            {
                string pw = GetMD5(password);
                if (isMD5) pw = password;
                return await Connections.HttpGetAsync<LoginModel>(HOST + $"/api/Watermark/Login?user={user}&pwd={pw}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return new API<LoginModel>() { data = new LoginModel { Message = ex.Message } };
            }
        }

        public string GetMD5(string password)
        {
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes(password));
            StringBuilder sb = new StringBuilder();
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            var pw = sb.ToString();
            return pw;
        }

        public async Task<bool> Download(string watermarkId)
        {
            try
            {
                using var client = new HttpClient();
                var stream = await client.GetStreamAsync($"http://cdn.thankful.top/{watermarkId}.zip");
                if (!Directory.Exists(Global.TemplatesFolder))
                {
                    Directory.CreateDirectory(Global.TemplatesFolder);
                }

                var target = Global.TemplatesFolder + watermarkId;
                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(target);
                }
                //File.WriteAllBytes(target + $"{Path.DirectorySeparatorChar}{watermarkId}.zip", stream);
                ZipFile.ExtractToDirectory(stream, target);

                await Connections.HttpGetAsync<bool>(HOST + $"/api/Watermark/Download?watermarkId={watermarkId}", Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }
    }

    public class LoginModel
    {
        public string Message { get; set; }
        public LoginChildModel data { get; set; }
        public string token { get; set; }
    }
    public class LoginChildModel
    {
        public string ID { get; set; }
        public string IMG { get; set; }
        public string DISPLAY_NAME { get; set; }
        public string USER_NAME { get; set; }
    }

    public class ZipedTemplate
    {
        public ZipedTemplate()
        {
            Images = new Dictionary<string, SKBitmap>();
            Fonts = new Dictionary<string, Stream>();
        }
        public WMCanvas WMCanvas { get; set; }
        public Dictionary<string, SKBitmap> Images { get; set; }
        public Dictionary<string, Stream> Fonts { get; set; }
        public SKBitmap Bitmap { get; set; }
        public string Src { get; set; }
        public string Desc { get; set; }
        public string WatermarkId { get; set; }
        public int DownloadTimes { get; set; }
        public int Coins { get; set; }
    }

    public class SysUser
    {
        public string ID { get; set; }
        public string USER_NAME { get; set; }
        public string DISPLAY_NAME { get; set; }
        public string PASSWORD { get; set; }
        public string PK_ID { get; set; }
    }
}
