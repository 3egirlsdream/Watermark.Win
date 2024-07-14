using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using Foundation;
using ObjCRuntime;
using PassKit;
using UIKit;

namespace Watermark.Platforms.iOS
{
    public class ApplePayAuthorizer : PKPaymentAuthorizationViewControllerDelegate, IApplePayAuthorizer
    {
        double PayAmount = 1;

        readonly NSString[] supportedNetworks ={
            PKPaymentNetwork.Amex,
            PKPaymentNetwork.Discover,
            PKPaymentNetwork.MasterCard,
            PKPaymentNetwork.Visa,
            PKPaymentNetwork.IDCredit,
            PKPaymentNetwork.Interac,
            PKPaymentNetwork.Jcb,
            PKPaymentNetwork.QuicPay,
            PKPaymentNetwork.ChinaUnionPay
        };

        bool isSuccess = false;

        public Action<string> PaymentSuccess { get; set; }
        public Action PaymentFailed { get; set; }

        public ApplePayAuthorizer() { }

        public void AuthorizePayment(double Amount)
        {
            if (!PKPaymentAuthorizationViewController.CanMakePaymentsUsingNetworks(supportedNetworks))
            {
                ShowAuthorizationAlert();
                return;
            }
            try
            {
                PayAmount = Amount;
                isSuccess = false;
                // Set up our payment request.
                var paymentRequest = new PKPaymentRequest();

                // Our merchant identifier needs to match what we previously set up in
                // the Capabilities window (or the developer portal).
                paymentRequest.MerchantIdentifier = "com.top.thankful.watermark.ios";

                // Both country code and currency code are standard ISO formats. Country
                // should be the region you will process the payment in. Currency should
                // be the currency you would like to charge in.
                paymentRequest.CountryCode = "AE";
                paymentRequest.CurrencyCode = "AED";

                // The networks we are able to accept.
                paymentRequest.SupportedNetworks = supportedNetworks;

                // Ask your payment processor what settings are right for your app. In
                // most cases you will want to leave this set to ThreeDS.
                paymentRequest.MerchantCapabilities = PKMerchantCapability.ThreeDS;
                //     AddToLog("Adding Item To Payment");

                // An array of `PKPaymentSummaryItems` that we'd like to display on the
                // sheet (see the MakeSummaryItems method).
                paymentRequest.PaymentSummaryItems = MakeSummaryItems(false);

                // Request shipping information, in this case just postal address.
                //  paymentRequest.RequiredShippingAddressFields = PKAddressField.PostalAddress;


                // Display the view controller.
                var viewController = new PKPaymentAuthorizationViewController(paymentRequest);
                viewController.Delegate = this;

                var rootController = WindowStateManager.Default.GetCurrentUIViewController();
                rootController.PresentViewController(viewController, true, null);

            }
            catch (Exception ex)
            {
                ShowAuthorizationAlert();
            }
        }

        PKPaymentSummaryItem[] MakeSummaryItems(bool requiresInternationalSurcharge)
        {
            var items = new List<PKPaymentSummaryItem>();

            var productSummaryItem = PKPaymentSummaryItem.Create("Sub-total", new NSDecimalNumber(PayAmount));
            items.Add(productSummaryItem);

            var totalSummaryItem = PKPaymentSummaryItem.Create("MauiApplePayment", productSummaryItem.Amount);
            items.Add(totalSummaryItem);

            return items.ToArray();
        }

        [Export("paymentAuthorizationViewController:didAuthorizePayment:handler:")]
        public override void DidAuthorizePayment2(PKPaymentAuthorizationViewController controller, PKPayment payment, Action<PKPaymentAuthorizationResult> completion)
        {
            var paymentToken = payment.Token;
            var data = payment.Token.PaymentData;
            var paymentResult = new PKPaymentAuthorizationResult(PKPaymentAuthorizationStatus.Success, null);
            completion(paymentResult);
            isSuccess = true;
            PaymentSuccess?.Invoke(data.ToString());
        }

        [Export("paymentAuthorizationViewControllerDidFinish:")]
        public override void PaymentAuthorizationViewControllerDidFinish(PKPaymentAuthorizationViewController controller)
        {
            if (!isSuccess) PaymentFailed?.Invoke();
            controller.DismissViewController(true, null);
        }

        void ShowAuthorizationAlert()
        {
            try
            {
                var alert = UIAlertController.Create("Error", "This device cannot make payments.", UIAlertControllerStyle.Alert);
                var action = UIAlertAction.Create("Okay", UIAlertActionStyle.Default, null);
                alert.AddAction(action);

                var rootController = WindowStateManager.Default.GetCurrentUIViewController();
                rootController.PresentViewController(alert, true, null);
                PaymentFailed?.Invoke();
            }
            catch (Exception ex) { Console.Write(ex); }
        }
    }
}
