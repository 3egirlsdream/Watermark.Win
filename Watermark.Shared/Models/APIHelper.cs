using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Qiniu.Http;
using Qiniu.Storage;
using SkiaSharp;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Watermark.Shared.Enums;

namespace Watermark.Win.Models
{
    public class APIHelper
    {
#if DEBUG
        public static string HOST = "http://thankful.top:4396";
#else
        public static string HOST = "http://thankful.top:4396";
#endif
        public APIHelper()
        {
            _client = new HttpClient() { BaseAddress =  new Uri(HOST) };
            client = new HttpClient();
        }
        HttpClient client;
        HttpClient _client;

        public async Task<API<bool?>> UploadWatermark(string watermarkId, string name, int coins, string desc = "")
        {
            //获取云端已有的字体文件
            var cloudFonts = await Connections.HttpGetAsync<List<WMCloudFont>>(HOST + $"/api/CloudSync/GetFontsList", Encoding.UTF8);
            var fonts = cloudFonts.success ? cloudFonts.data.Select(x=>x.NAME).ToList() : [];
            //剔除无用的文件
            var folder = Global.AppPath.TemplatesFolder + watermarkId + Path.DirectorySeparatorChar;
            if (Directory.Exists(folder))
            {
                var json = File.ReadAllText(folder + "config.json");
                var direct = new DirectoryInfo(folder);
                var files = direct.GetFiles();
                foreach (var file in files)
                {
                    try
                    {
                        if (file.Extension != ".json" && !file.Name.Contains("default") && !json.Contains(file.Name))
                        {
                            file.Delete();
                        }
                        else if (file.Extension == ".otf" || file.Extension == ".ttf")
                        {
                            if (!fonts.Any(c => c == file.Name))
                            {
                                var r = await UploadFontToQiniu(watermarkId, file.Name);
                                if (r.success)
                                {
                                    await Connections.HttpGetAsync<List<string>>(HOST + $"/api/CloudSync/UploadPath?Name={file.Name}&Url=https://cdn.thankful.top/{file.Name}&Url_B=https://cdn.thankful.top/{file.Name}", Encoding.UTF8);
                                }
                            }
                            file.Delete();

                        }
                    }
                    catch { }
                }
            }

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
            var path = Global.AppPath.TemplatesFolder + watermarkId;
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

                var targetPath = Global.AppPath.TemplatesFolder + $"{watermarkId}.zip";
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
                return new API<string> { success = result.Code == 200, message = new APISub { content = result.Text } };
            }
            return new API<string>() { success = false, message = new APISub() { content = "文件不存在" } };
        }

        public async Task<API<string>> UploadFontToQiniu(string watermarkId, string fname)
        {
            var path = Global.AppPath.TemplatesFolder + watermarkId;
            if (Directory.Exists(path))
            {
                var request = await Connections.HttpGetAsync<string>(HOST + $"/api/Qiniu/GetToken?key={fname}", Encoding.UTF8);
                if (!request.success) return request;
                var token = request.data ?? "";

                var targetPath = path + Path.DirectorySeparatorChar + fname;

                Config config = new Config
                {
                    // 设置上传区域
                    Zone = Zone.ZONE_CN_South,
                    // 设置 http 或者 https 上传
                    UseHttps = true,
                    UseCdnDomains = true,
                    ChunkSize = ChunkUnit.U512K
                };
                // 表单上传
                FormUploader target = new FormUploader(config);
                HttpResult result = target.UploadFile(targetPath, fname, token, null);
                var rst = new API<string>
                {
                    success = result.Code == 200,
                    message = new APISub { content = result.Text }
                };
                return rst;
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
                        t.Recommend = Convert.ToInt32(item?["RECOMMEND"]?.ToString() ?? "0") > 0 ? true : false;
                        t.UserDisplayName = item?["DISPLAY_NAME"]?.ToString();
                        t.Visible = item?["STATE"]?.ToString() == "A";
                        if (DateTime.TryParse(item?["DATETIME_CREATED"]?.ToString(), out var dt))
                        {
                            t.DateTimeCreated = dt;
                        }
                        else t.DateTimeCreated = DateTime.Now;
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
            try
            {
                var stream = await client.GetStreamAsync($"https://cdn.thankful.top/{watermarkId}.zip");
                using MemoryStream ms = new MemoryStream();
                await stream.CopyToAsync(ms);
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
                    }
                    else if (entry.FullName.EndsWith("default.jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        using var entryStream = entry.Open();
                        SKBitmap sKBitmap = SKBitmap.Decode(entryStream);
                        t.Bitmap = sKBitmap;
                    }
                    else if (entry.FullName.EndsWith(".ttf") || entry.FullName.EndsWith(".otf"))
                    {
                        //var entryStream = entry.Open();
                        //using MemoryStream mss = new();
                        //entryStream.CopyTo(mss);
                        //t.Fonts[entry.FullName] = mss.ToArray();
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
            catch (Exception ex)
            {
                return default;
            }
        }


        public async Task<API<WMSysUser>> Register(WMSysUser user)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(HOST);
                var password = GetMD5(user.PASSWORD);

                var formContent = new
                {
                    username = user.USER_NAME,
                    displayname = user.DISPLAY_NAME,
                    password = password,
                    pkid = user.PK_ID
                };

                using var response = await client.PostAsJsonAsync("/api/Watermark/SignUp", formContent);
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
            var target = Global.AppPath.TemplatesFolder + watermarkId;
            if (Directory.Exists(target)) return true;
            return false;
        }

        public async Task<bool> Download(string watermarkId, string watermarkUserId)
        {
            try
            {
                using var client = new HttpClient();
                using (var stream = await client.GetStreamAsync($"http://cdn.thankful.top/{watermarkId}.zip"))
                {
                    if (!Directory.Exists(Global.AppPath.TemplatesFolder))
                    {
                        Directory.CreateDirectory(Global.AppPath.TemplatesFolder);
                    }

                    var target = Global.AppPath.TemplatesFolder + watermarkId;
                    if (Directory.Exists(target))
                    {
                        Directory.Delete(target, true);
                    }
                    Directory.CreateDirectory(target);
                    await Task.Run(() => ZipFile.ExtractToDirectory(stream, target, true));
                }
                if (Global.CurrentUser != null && !string.IsNullOrEmpty(Global.CurrentUser.ID) && Global.CurrentUser.ID != watermarkUserId)
                {
                    await Connections.HttpGetAsync<bool>(HOST + $"/api/Watermark/Download?watermarkId={watermarkId}", Encoding.UTF8);
                }
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public async Task<bool> Downloads(List<string> watermarkIds)
        {
            try
            {
                foreach (var watermarkId in watermarkIds)
                {
                    using (var stream = await client.GetStreamAsync($"http://cdn.thankful.top/{watermarkId}.zip"))
                    {
                        if (!Directory.Exists(Global.AppPath.MarketFolder))
                        {
                            Directory.CreateDirectory(Global.AppPath.MarketFolder);
                        }

                        var target = Global.AppPath.MarketFolder + watermarkId;
                        if (Directory.Exists(target))
                        {
                            Directory.Delete(target, true);
                        }
                        Directory.CreateDirectory(target);
                        await Task.Run(() => ZipFile.ExtractToDirectory(stream, target, true));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }


        public async Task<bool> DownloadAndorid(List<string> watermarkIds)
        {
            try
            {
                foreach (var watermarkId in watermarkIds)
                {
                    using (var stream = await client.GetStreamAsync($"http://cdn.thankful.top/{watermarkId}.zip"))
                    {
                        if (!Directory.Exists(Global.AppPath.MarketFolder))
                        {
                            Directory.CreateDirectory(Global.AppPath.MarketFolder);
                        }

                        var target = Global.AppPath.MarketFolder + watermarkId;
                        if (Directory.Exists(target))
                        {
                            Directory.Delete(target, true);
                        }
                        Directory.CreateDirectory(target);
                        await Task.Run(() => ZipFile.ExtractToDirectory(stream, target, true));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }


        public async Task DownloadLogoes()
        {
            try
            {
                string logoUri = "Logoes";
                if (Directory.Exists(Global.AppPath.LogoesFolder)) return;
                Directory.CreateDirectory(Global.AppPath.LogoesFolder);
                using (var stream = await client.GetStreamAsync($"http://cdn.thankful.top/{logoUri}.zip"))
                {
                    var target = Global.AppPath.LogoesFolder;
                    await Task.Run(() => ZipFile.ExtractToDirectory(stream, target, true));
                }

            }
            catch (Exception ex)
            {
            }
        }



        public async Task<Tuple<bool, bool>> TemplateIsExsist(string watermarkId, string? userId)
        {
            try
            {
                var result = await Connections.HttpGetAsync<dynamic>(HOST + $"/api/Watermark/TemplateIsExsist?watermarkId={watermarkId}&userId={userId}", Encoding.UTF8);
                if (result == null || !result.success)
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

        public async Task<bool> AddILike(string userId, string watermarkId)
        {
            var result = await Connections.HttpGetAsync<bool>(HOST + $"/api/Watermark/AddILike?userId={userId}&watermarkId={watermarkId}", Encoding.UTF8);
            if (result != null) return result.data;
            else return false;
        }

        public async void DeleteILike(string userId, string watermarkId)
        {
            await Connections.HttpGetAsync<bool>(HOST + $"/api/Watermark/DeleteILike?userId={userId}&watermarkId={watermarkId}", Encoding.UTF8);
        }

        public async Task<API<List<WMZipedTemplate>>> GetILike(string userId)
        {
            var result = await Connections.HttpGetAsync<List<WMZipedTemplate>>(HOST + $"/api/Watermark/GetILike?userId={userId}", Encoding.UTF8);
            return result;
        }

        public async Task<API<List<string>>> GetISubscribed(string userId)
        {
            var result = await Connections.HttpGetAsync<List<string>>(HOST + $"/api/Watermark/GetISubscribed?userId={userId}", Encoding.UTF8);
            return result;
        }

        public async Task<API<bool>> SubscribeUser(string userId, string subscribedId)
        {
            var result = await Connections.HttpGetAsync<bool>(HOST + $"/api/Watermark/SubscribeUser?userId={userId}&subscribedId={subscribedId}", Encoding.UTF8);
            return result;
        }
        public async Task<API<bool>> TakeOffOnWatermark(string userId, string watermarkId)
        {
            var result = await Connections.HttpGetAsync<bool>(HOST + $"/api/Watermark/TakeOffOnWatermark?userId={userId}&watermarkId={watermarkId}", Encoding.UTF8);
            return result;
        }

        public async Task DownloadFonts(List<string> fonts)
        {
            var p = Global.AppPath.BasePath + "fonts";
            if (!Directory.Exists(p))
            {
                Directory.CreateDirectory(p);
            }

            foreach (var key in fonts)
            {
                try
                {
                    if(!key.Contains(".")) continue;
                    var path = Path.Combine(p, key);
                    if (File.Exists(path))
                    {
                        using var typeface = SKTypeface.FromFile(path);
                        if (typeface != null) continue;
                        else
                        {
                            File.Delete(path);
                        }
                    };
                    using var stream = await _client.GetStreamAsync($"https://cdn.thankful.top/{key}");
                    using var fs = File.Create(path);
                    await stream.CopyToAsync(fs);
                    fs.Close();
                    fs.Dispose();
                }
                catch(Exception ex) 
                { 
                }
            }
		}

		public async Task<API<bool>> PageVisitRecord(ProgramPage page, Platform pf)
		{
            string pageName = Enum.GetName(typeof(ProgramPage), page);
            string platform = Enum.GetName(typeof(Platform), pf);

            var result = await Connections.HttpGetAsync<bool>(HOST + $"/api/Watermark/PageVisitRecord?pageName={pageName}&platform={platform}", Encoding.UTF8);
			return result;
		}

	}
}
