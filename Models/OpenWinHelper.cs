using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Watermark.Win.Views;

namespace Watermark.Win.Models
{
    public static class OpenWinHelper
    {
        public static void Open(Action action)
        {
            void ExecuteWithObject(Action a)
            {
                a.Invoke();
            }

            SynchronizationContext.Current!.Post(_ => { ExecuteWithObject(action); }, null);
        }
    }
}
