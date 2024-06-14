using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watermark.Andorid.Platforms.iOS
{
    public interface IApplePayAuthorizer
    {
        void AuthorizePayment(double amount);
        Action<string> PaymentSuccess { get; set; }
        Action PaymentFailed { get; set; }
    }
}
