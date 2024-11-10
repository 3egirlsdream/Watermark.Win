using System;
using System.Diagnostics;
//using AlipaySDK_ApiDefinition;
using Foundation;
using UIKit;

namespace Watermark
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override bool FinishedLaunching(UIApplication uiApplication, NSDictionary options)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException; 
            return base.FinishedLaunching(uiApplication, options);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
        }

        public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
        {
            /*AlipaySDK.DefaultService.ProcessOrderWithPaymentResult(url, resultDic =>
            {
                IDictionary<string, string> result = new Dictionary<string, string>();
                resultDic.Keys?.ToList().ForEach(key =>
                {
                    var getOk = resultDic.TryGetValue(key, out NSObject obj);
                    if (getOk)
                    {
                        result.Add(key.ToString(), obj.ToString());
                    }

                    Debug.WriteLine($"{result.Keys.FirstOrDefault()},{result.Values.FirstOrDefault()}");
                });
            });*/

            return true;
        }
    }
}
