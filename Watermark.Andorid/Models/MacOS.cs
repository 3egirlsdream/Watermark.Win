using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watermark.Andorid.Models
{
    public static class MacOS
    {

        public static Dictionary<DevicePlatform, IEnumerable<string>> FileType = new()
          {
            { DevicePlatform.Android, new[] { "image/*" } } ,
            { DevicePlatform.iOS, new[] { "public.jpeg", "public.png", "public.image" } },
            { DevicePlatform.MacCatalyst, new[] { "public.jpeg", "public.png", "public.image" } },
            { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png" } }
          };
        public static Dictionary<DevicePlatform, IEnumerable<string>> FileFontType = new()
          {
            { DevicePlatform.Android, new[] { ".ttf", ".otf" } } ,
            { DevicePlatform.iOS, new[] { ".ttf", ".otf" } },
            { DevicePlatform.MacCatalyst, new[] { ".ttf", ".otf" } },
            { DevicePlatform.WinUI, new[] { ".ttf", ".otf" } }
          };
    }
}
