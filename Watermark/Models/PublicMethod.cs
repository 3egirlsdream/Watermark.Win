//using AlipaySDK_ApiDefinition;
//using DemoUtilsBinding;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sharpen;
using System.Text.RegularExpressions;
using Watermark.Shared.Models;


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
#if IOS
                    string con = rs.data;
                    // NOTE: 如果加签成功，则继续执行支付
                    if (!string.IsNullOrEmpty(con))
                    {
                        //应用注册scheme,在AliSDKDemo-Info.plist定义URL types
                        var appScheme = "com.top.thankful.watermark.ios";

                        // NOTE: 将签名成功字符串格式化为订单字符串,请严格按照该格式

                        // NOTE: 调用支付结果开始支付
                        /*AlipaySDK.DefaultService.PayOrder(con, appScheme, resultDic =>
                        {
                            var result = resultDic;

                            //ios9.0以及后续新版本需要在AppDelegate.cs中的OpenUrl获取回调信息
                        });*/
                    }
                    return new API<string> { };
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
        /*
        void a()
        {
            var util = new DemoUtils();
            var tradeNo = util.GenerateTradeNO;

            //将商品信息赋予AlixPayOrder的成员变量
            var order = new APOrderInfo
            {
                // NOTE: app_id设置
                App_id = "test",

                // NOTE: 支付接口名称
                Method = "alipay.trade.app.pay",

                // NOTE: 参数编码格式
                Charset = "utf-8",

                // NOTE: 当前时间点
                Timestamp = string.Format("yyyy-MM-dd HH:mm:ss", DateTime.Now),

                // NOTE: 支付版本
                Version = "1.0",

                //NOTE: sign_type设置
                Sign_type = "RSA",

                // NOTE: 商品数据
                Biz_content = new APBizContent
                {
                    Body = "我是测试数据",
                    Subject = "1",
                    Out_trade_no = tradeNo,
                    Timeout_express = "30ms",//超时时间设置
                    Total_amount = string.Format("%.2f", 0.01)//商品价格
                }
            };

            //将商品信息拼接成字符串
            var orderInfo = "";
            var orderinfoEncoded = "";
            orderInfo = order.OrderInfoEncoded(false);
            orderinfoEncoded = order.OrderInfoEncoded(true);

            // NOTE: 获取私钥并将商户信息签名，外部商户的加签过程请务必放在服务端，防止公私钥数据泄露；
            //       需要遵循RSA签名规范，并将签名字符串base64编码和UrlEncode

            var privateKey = "你的private key";

            var signer = new APRSASigner(privateKey);

            var signerStr = signer.SignString(orderInfo, true);

            // NOTE: 如果加签成功，则继续执行支付
            if (!string.IsNullOrEmpty(signerStr))
            {
                //应用注册scheme,在AliSDKDemo-Info.plist定义URL types
                var appScheme = "alisdkdemo";

                // NOTE: 将签名成功字符串格式化为订单字符串,请严格按照该格式
                var orderString = string.Format("%@&sign=%@", orderinfoEncoded, signerStr);

                // NOTE: 调用支付结果开始支付
                AlipaySDK.DefaultService.PayOrder(orderString, appScheme, resultDic =>
                {
                    var result = resultDic;

                    //ios9.0以及后续新版本需要在AppDelegate.cs中的OpenUrl获取回调信息
                });
            }
        }*/
    }
}