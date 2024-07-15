using Microsoft.JSInterop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sharpen;
using System.Text.RegularExpressions;
using Watermark.Win.Models;

namespace Watermark.Models
{
    public static class PublicMethods
    {
        public static async Task<API<string>> AliPays(decimal cost, string tradeName)
        {
            return new API<string> { };
        }

        public static async Task ReLogin()
        {
            APIHelper helper = new APIHelper();
            var result = await Global.ReadLocalAsync();
            if (!string.IsNullOrEmpty(result.Item1))
            {
                var login = await helper.LoginIn(result.Item1, result.Item2, true);
                if (login.success)
                {
                    Global.CurrentUser = Global.SetUserInfo(login.data.data);
                }
            }
        }
    }
}