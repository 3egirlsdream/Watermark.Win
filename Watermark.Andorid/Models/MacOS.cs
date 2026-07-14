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
            { DevicePlatform.Android, new[] { "image/*", "image/x-adobe-dng", "image/x-canon-cr2", "image/x-canon-cr3", "image/x-nikon-nef", "image/x-sony-arw", "image/x-fuji-raf", "image/x-olympus-orf", "image/x-panasonic-rw2" } } ,
            { DevicePlatform.iOS, new[] { "public.image", "public.camera-raw-image", "com.adobe.raw-image" } },
            { DevicePlatform.MacCatalyst, new[] { "public.image", "public.camera-raw-image", "com.adobe.raw-image" } },
            { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".tif", ".tiff", ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".sr2", ".raf", ".orf", ".rw2", ".rwl", ".pef", ".3fr", ".iiq", ".srw" } }
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
