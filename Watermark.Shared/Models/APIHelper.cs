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
#if DEBUG
        public static string HOST = "https://localhost:44389";
#else
        public static string HOST = "http://thankful.top:4396";
#endif
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
                if (File.Exists(configPath) && !string.IsNullOrEmpty(name))
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

        public async Task<List<WMZipedTemplate>> GetWatermarks(string user, int start, int length, string desc = "countDesc")
        {
            try
            {
                using HttpResponseMessage response = await _client.GetAsync($"/api/Watermark/GetWatermarks?userId={user}&start={start}&length={length}&type={desc}");
                response.EnsureSuccessStatusCode();
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseObject = JsonConvert.DeserializeObject<API<dynamic>>(responseContent);

                if (responseObject != null && responseObject.success)
                {
                    var total = responseObject.data.Total;

                    var data = (JArray)responseObject.data.Data;
                    if (data == null) return [];
                    List<WMZipedTemplate> templates = new List<WMZipedTemplate>();
                    List<Task> tasks = new List<Task>();
                    foreach (var item in data)
                    {
                        var t = new WMZipedTemplate();
                        t.WatermarkId = item?["ID"]?.ToString();
                        t.UserId = item?["USER_ID"]?.ToString();
                        t.Desc = item?["DESC"]?.ToString();
                        t.Coins = Convert.ToInt32(item?["COINS"]?.ToString() ?? "0");
                        t.DownloadTimes = Convert.ToInt32(item?["DOWNLOAD_TIMES"]?.ToString() ?? "0");
                        templates.Add(t);
                    }

                    return templates;
                }
                else
                {
                    return new List<WMZipedTemplate>();
                }
            }
            catch (Exception ex)
            {
                return [];
            }
        }
        
        public async Task<WMZipedTemplate> ExtractZip(string watermarkId)
        {
            using var client = new HttpClient();
            var stream = await client.GetStreamAsync($"https://cdn.thankful.top/{watermarkId}.zip");
            using MemoryStream ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);
            using ZipArchive archive = new ZipArchive(ms, ZipArchiveMode.Read);
            WMZipedTemplate t = new WMZipedTemplate();
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
                    using MemoryStream mss = new();
                    entryStream.CopyTo(mss);
                    SKBitmap sKBitmap = SKBitmap.Decode(mss.ToArray());
                    t.Images[entry.FullName] = sKBitmap;
                }
            }
            return t;
        }


        public async Task<API<WMSysUser>> Register(WMSysUser user)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(HOST);
                var password = GetMD5(user.PASSWORD);
                
                var formContent = new {
                    username = user.USER_NAME,
                    displayname = user.DISPLAY_NAME,
                    password = password,
                    pkid = user.PK_ID
                };

                var response = await client.PostAsJsonAsync("/api/Watermark/SignUp", formContent);
                var bt = await response.Content.ReadAsByteArrayAsync();
                var str = Encoding.UTF8.GetString(bt);
                var result = JsonConvert.DeserializeObject<API<WMSysUser>>(str);
                if (response.IsSuccessStatusCode)
                {
                    return result;
                }
                else return new API<WMSysUser>() { success = false };
            }
        }
    
        public async Task<API<WMLoginModel>> LoginIn(string user, string password, bool isMD5 = false)
        {
            try
            {
                string pw = GetMD5(password);
                if (isMD5) pw = password;
                return await Connections.HttpGetAsync<WMLoginModel>(HOST + $"/api/Watermark/Login?user={user}&pwd={pw}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return new API<WMLoginModel>() { data = new WMLoginModel { Message = ex.Message } };
            }
        }

        public string GetMD5(string password)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(password));

        }

        public bool FolderExsist(string watermarkId)
        {
            var target = Global.TemplatesFolder + watermarkId;
            if(Directory.Exists(target)) return true;
            return false;
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
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }
                Directory.CreateDirectory(target);
                //File.WriteAllBytes(target + $"{Path.DirectorySeparatorChar}{watermarkId}.zip", stream);
                ZipFile.ExtractToDirectory(stream, target, true);

                await Connections.HttpGetAsync<bool>(HOST + $"/api/Watermark/Download?watermarkId={watermarkId}", Encoding.UTF8);
                return true;
            }
            catch(Exception ex) 
            { 
                return false; 
            }
        }


        public async Task<Tuple<bool, bool>> TemplateIsExsist(string watermarkId, string? userId)
        {
            try
            {
                var result = await Connections.HttpGetAsync<dynamic>(HOST + $"/api/Watermark/TemplateIsExsist?watermarkId={watermarkId}&userId={userId}", Encoding.UTF8);
                if(result == null || !result.success ) 
                {
                    throw new Exception("");
                }
                var exsist = (bool)result.data.exsist;
                var self = (bool)result.data.self;
                return Tuple.Create(exsist, self);
            }
            catch (Exception ex)
            {
                Tuple<bool, bool> tuple = Tuple.Create(false, false);
                return tuple;
            }
        }
    }
}
