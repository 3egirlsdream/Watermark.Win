using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watermark.Win.Models
{
    public interface IWindowService
    {
        void Minimize();
        void Maximize();
        bool IsMaximized();
        void Close(bool allWindow = false);
    }
}
