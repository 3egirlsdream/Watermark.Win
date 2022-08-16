using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WeakToys.Class
{
    public class Connections
    {
        public static API<T> HttpGet<T>(string url, string contentType = null, Dictionary<string, string> headers = null)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    if (contentType != null)
                        client.DefaultRequestHeaders.Add("ContentType", contentType);
                    if (headers != null)
                    {
                        foreach (var header in headers)
                            client.DefaultRequestHeaders.Add(header.Key, header.Value);
                    }
                    HttpResponseMessage response = client.GetAsync(url).Result;
                    var rst = response.Content.ReadAsStringAsync().Result;
                    return JsonConvert.DeserializeObject<API<T>>(rst);
                }
                catch(Exception ex)
                {
                    return new API<T>
                    {
                        code = 200,
                        success = false,
                        data = default,
                        message = new APISub { content = ex.Message }
                    };
                }
            }
        }

        public static async Task<API<T>> HttpGetAsync<T>(string url, Encoding encoding = null)
        {
            try
            {
                HttpClient httpClient = new HttpClient();
                var data = await httpClient.GetByteArrayAsync(url);
                var ret = encoding.GetString(data);
                var result = JsonConvert.DeserializeObject<API<T>>(ret);
                return result;
            }
            catch (Exception ex)
            {
                return new API<T>
                {
                    code = 200,
                    success = false,
                    data = default,
                    message = new APISub { content = ex.Message }
                };
            }
        }

        public static API<T> Post<T>(string url, object postData = null)
        {
            try
            {
                HttpClient httpClient = new HttpClient();//http对象
                HttpResponseMessage response = httpClient.PostAsync(url, new JsonContent(postData)).Result;
                string ret = response.Content.ReadAsStringAsync().Result;
                var result = JsonConvert.DeserializeObject<API<T>>(ret);
                return result;
            }
            catch(Exception ex)
            {
                return new API<T>
                {
                    code = 200,
                    success = false,
                    data = default,
                    message = new APISub { content = ex.Message }
                };
            }
        }
    }

    public class JsonContent : StringContent
    {
        public JsonContent(object obj) :
        base(JsonConvert.SerializeObject(obj), Encoding.UTF8, "application/json")
        { }
    }


    public class API<T>
    {
        public int code { get; set; }
        public bool success { get; set; }
        public T data { get; set; }
        public APISub message { get; set; }
    }

    public class APISub
    {
        public string content { get; set; }
    }
}
