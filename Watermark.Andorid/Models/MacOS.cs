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
            { DevicePlatform.Android, new[] { "text/*" } } ,
            { DevicePlatform.iOS, new[] { "public.json", "public.plain-text" } },
            { DevicePlatform.MacCatalyst, new[] { "image/jpg", "image/png" } },
            { DevicePlatform.WinUI, new[] { ".jpg", ".jepg" } }
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
