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
            var api = new APIHelper();
            var rs = await api.GetPayToken(cost, tradeName);
            if (rs != null && rs.success && !string.IsNullOrEmpty(rs.data))
            {
                API<string> jt = await Task.Run(() =>
                {
#if ANDROID
                    string con = rs.data;
                    var act = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                    Com.Alipay.Sdk.App.PayTask pa = new Com.Alipay.Sdk.App.PayTask(act);
                    var payRs = pa.Pay(con, true);
                    try
                    {
                        string json = payRs.ToString();
                        if (json.StartsWith("resultStatus={9000}"))
                        {
                            var start = json.IndexOf("result={");
                            var content = json.Substring(start + 8);
                            var end = content.LastIndexOf("};extendInfo=");
                            content = content.Substring(0, end);
                            return new API<string> { success = true, data = content };
                        }
                        else return new API<string> { success = false, message = new APISub { content = $"支付失败：错误代码" } };
                    }
                    catch (Exception ex)
                    {
                        return new API<string> { message = new APISub { content = ex.Message }, success = false };

                    }
#else
                    return new API<string> { };
#endif
                });
                if (!jt.success) return jt;

                JObject result = JObject.Parse(jt.data);
                var r = result?["alipay_trade_app_pay_response"];
                if (r == null) return new API<string> { success = false, message = new APISub { content = "API返回为空" } };
                var code = r["code"]?.ToString();
                var msg = r["msg"]?.ToString();
                var app_id = r["app_id"]?.ToString();
                var auth_app_id = r["auth_app_id"]?.ToString();
                var out_trade_no = r["out_trade_no"]?.ToString(); ;
                var total_amount = r["total_amount"]?.ToString();
                var trade_no = r["trade_no"]?.ToString();
                var seller_id = r["seller_id"]?.ToString();
                var up = await api.RecordBill(code, msg, app_id, auth_app_id, out_trade_no, trade_no, tradeName, total_amount, seller_id);
                if (up == null || !up.success) return new API<string> { success = false, message = new APISub { content = up?.message?.content ?? "" } };

                return new API<string> { success = true, data = "支付成功" };
            }
            else
            {
                return rs;
            }

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