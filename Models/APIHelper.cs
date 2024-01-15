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
using static MudBlazor.Colors;

namespace Watermark.Win.Models
{
    public class APIHelper
    {
        private string HOST = "https://localhost:44389";
        private string CUR_ID = "9DEBF7DC-F58C-4667-BACF-A6BFD18352EB";
        public APIHelper()
        {
            _client = new HttpClient() { BaseAddress =  new Uri(HOST) };
        }

        HttpClient _client;

        public async Task<bool> UploadWatermark(string watermarkId)
        {
            var path = Global.TemplatesFolder + watermarkId;
            if (Directory.Exists(path))
            {
                var target = Global.TemplatesFolder + $"{watermarkId}.zip";
                if (File.Exists(target)) File.Delete(target);
                ZipFile.CreateFromDirectory(path, target);
                using (var form = new MultipartFormDataContent())
                {
                    using (var fileStream = new FileStream(target, FileMode.Open))
                    {
                        form.Add(new StreamContent(fileStream), "file", Path.GetFileName(target));
                        form.Add(new StringContent(watermarkId), "watermarkId");
                        form.Add(new StringContent(""), "desc");
                        form.Add(new StringContent(CUR_ID), "userId");
                        using (var response = await _client.PostAsync("/api/Watermark/Upload", form))
                        {
                            return response.IsSuccessStatusCode;
                        }
                    }
                }
            }
            else return false;
        }

        public async Task<List<ZipedTemplate>> GetWatermarks(int start, int length)
        {
            var userId = "9DEBF7DC-F58C-4667-BACF-A6BFD18352EB";
            using (HttpResponseMessage response = await _client.GetAsync($"/api/Watermark/GetWatermarks?start=0&length=10"))
            {
                response.EnsureSuccessStatusCode();
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseObject = JsonConvert.DeserializeObject<API<dynamic>>(responseContent);

                var total = responseObject.data.Total;

                var fileResults = (JArray)responseObject.data.Files;
                List<byte[]> filesData = new List<byte[]>();
                foreach (var fileResult in fileResults)
                {
                    var content = fileResult["FileContents"]?.ToString();
                    var fileContents = Convert.FromBase64String(content);
                    filesData.Add(fileContents);
                }

                List<ZipedTemplate> templates = new List<ZipedTemplate>();
                foreach (var fileResult in filesData)
                {
                    var t = ExtractZip(fileResult);
                    templates.Add(t);
                }
                return templates;
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


        public async Task<API<bool>> Register(SysUser user)
        {
            using (var client = new HttpClient())
            {
                var bytes = MD5.HashData(Convert.FromBase64String(user.PASSWORD));
                StringBuilder sb = new StringBuilder();
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                var password = sb.ToString();
                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("username", user.USER_NAME),
                    new KeyValuePair<string, string>("displayname", user.DISPLAY_NAME),
                    new KeyValuePair<string, string>("password", password),
                    new KeyValuePair<string, string>("pkid", user.PK_ID)
                });

                var response = await client.PostAsync("/api/Watermark/SignUp", formContent);
                var result = await response.Content.ReadFromJsonAsync<API<bool>>();
                if (response.IsSuccessStatusCode)
                {
                    return result;
                }
                else return new API<bool>() { success = false };
            }
        }
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
    }

    public class SysUser
    {
        public string USER_NAME { get; set; }
        public string DISPLAY_NAME { get; set; }
        public string PASSWORD { get; set; }
        public string PK_ID { get; set; }
    }
}
