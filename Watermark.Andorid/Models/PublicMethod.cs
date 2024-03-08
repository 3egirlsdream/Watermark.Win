using Microsoft.JSInterop;
using Watermark.Win.Models;

namespace Watermark.Andorid.Models
{
    public static class PublicMethods
    {

        [JSInvokable]
        public static async Task AliPays(IJSObjectReference objRef, string aliPayStrs)
        {
#if ANDROID

            _ =Task.Run(async () =>
            {

                string con = aliPayStrs;//调用支付宝app支付接口返回的内容　　　　　　　　　 
                var act = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                Com.Alipay.Sdk.App.PayTask pa = new Com.Alipay.Sdk.App.PayTask(act);
                var result = pa.PayV2(con, true);
                var resultStatus = result.TryGetValue("resultStatus", out string resultStatusDic) ? resultStatusDic : "-1";
                var memo = result.TryGetValue("memo", out string memoDic) ? memoDic : "";

                if (resultStatus == "9000")
                {

                    memo = "支付成功";

                }
                else if (resultStatus == "-1")
                {
                    memo = "支付失败";
                }
                //执行前端html window上注册的回调方法
                await objRef.InvokeVoidAsync("aliPayCallBack", new { resultStatus = resultStatus, memo = memo });

            });
#endif
        }

        [JSInvokable]
        public static async Task ReLogin()
        {
            APIHelper helper = new APIHelper();
            var result = await Global.ReadLocalAsync();
            if (!string.IsNullOrEmpty(result.Item1))
            {
                var login = await helper.LoginIn(result.Item1, result.Item2, true);
                if (login.success)
                {
                    Global.CurrentUser = new WMLoginChildModel
                    {
                        ID = login.data.data.ID,
                        IMG = login.data.data.IMG,
                        DISPLAY_NAME = login.data.data.DISPLAY_NAME,
                        USER_NAME = login.data.data.USER_NAME,
                        EXPIRE_DATE = login.data.data.EXPIRE_DATE
                    };
                }
            }
        }
    }
}