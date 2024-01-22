using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public static void OpenFolder(Action action) 
        { 
            action?.Invoke();
        }
    }
}
