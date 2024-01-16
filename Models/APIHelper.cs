using MudBlazor.Charts.SVG.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using static MudBlazor.Colors;

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
            var path = Global.TemplatesFolder + watermarkId;
            if (Directory.Exists(path))
            {
                var config = path + Path.DirectorySeparatorChar + "config.json";
                if (File.Exists(config))
                {
                    var wc = Global.ReadConfig(config);
                    wc.Name = name;
                    File.Delete(config);
                    var json = Global.CanvasSerialize(wc);
                    System.IO.File.WriteAllText(config, json);
                }

                var target = Global.TemplatesFolder + $"{watermarkId}.zip";
                if (File.Exists(target)) File.Delete(target);
                ZipFile.CreateFromDirectory(path, target);
                using (var form = new MultipartFormDataContent())
                {
                    using (var fileStream = new FileStream(target, FileMode.Open))
                    {
                        form.Add(new StreamContent(fileStream), "file", Path.GetFileName(target));
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
                        else return new API<bool?>() { success = false };
                    }
                }
            }
            else return new API<bool?>() { success = false };
        }

        public async Task<List<ZipedTemplate>> GetWatermarks(int start, int length, string desc = "countDesc")
        {
            try
            {
                using (HttpResponseMessage response = await _client.GetAsync($"/api/Watermark/GetWatermarks?userId={Global.CurrentUser.ID}&start={start}&length={length}&type={desc}"))
                {
                    response.EnsureSuccessStatusCode();
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var responseObject = JsonConvert.DeserializeObject<API<dynamic>>(responseContent);

                    if (responseObject != null && responseObject.success)
                    {
                        var total = responseObject.data.Total;

                        var fileResults = (JArray)responseObject.data.Files;
                        List<ZipedTemplate> templates = new List<ZipedTemplate>();
                        List<Task> tasks = new List<Task>();    
                        foreach (var fileResult in fileResults)
                        {
                            var content = fileResult?["File"]?["FileContents"]?.ToString() ?? "";
                            var fileContents = Convert.FromBase64String(content);
                            var t = ExtractZip(fileContents);
                            t.WatermarkId = fileResult?["File"]?["ID"]?.ToString();
                            t.Desc = fileResult?["File"]?["DESC"]?.ToString();
                            t.DownloadTimes = Convert.ToInt32(fileResult?["File"]?["DOWNLOAD_TIMES"]?.ToString() ?? "0");
                            templates.Add(t);
                        }

                        return templates;
                    }
                    else
                    {
                        return new List<ZipedTemplate>();
                    }
                }
            }
            catch (Exception ex)
            {
                return new List<ZipedTemplate>();
            }
        }

        ZipedTemplate ExtractZip(byte[] zipBytes)
        {
            using (MemoryStream memoryStream = new MemoryStream(zipBytes))
            {
                using (ZipArchive archive = new ZipArchive(memoryStream, ZipArchiveMode.Read))
                {
                    ZipedTemplate t = new ZipedTemplate();
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith("config.json", StringComparison.OrdinalIgnoreCase))
                        {
                            using (var entryStream = entry.Open())
                            {
                                using (StreamReader reader = new StreamReader(entryStream))
                                {
                                    string content = reader.ReadToEnd();
                                    t.WMCanvas = Global.ReadConfig(content);
                                    Console.WriteLine($"Content of {entry.FullName}: {content}");
                                }
                            }
                        }
                        else if (entry.FullName.EndsWith("default.jpg", StringComparison.OrdinalIgnoreCase))
                        {
                            using var entryStream = entry.Open();
                            SKBitmap sKBitmap = SKBitmap.Decode(entryStream);
                            t.Bitmap = sKBitmap;
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
            }
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
        }
        public WMCanvas WMCanvas { get; set; }
        public Dictionary<string, SKBitmap> Images { get; set; }
        public SKBitmap Bitmap { get; set; }
        public string Src { get; set; }
        public string Desc { get; set; }
        public string WatermarkId { get; set; }
        public int DownloadTimes { get; set; }
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
