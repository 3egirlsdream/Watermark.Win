using ExifLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watermark.Win.Models
{
    public static class ExifHelper
    {
        public static Dictionary<string, string> ReadImage(string path)
        {
            ExifReader exifReader = new ExifReader(path);
            var dic = new Dictionary<string, string>();
            foreach(var key in Enum.GetNames(typeof(ExifTags)))
            {
                var e = (ExifTags)Enum.Parse(typeof(ExifTags), key);
                if (exifReader.GetTagValue(e, out object result))
                {
                    dic[key] = result.ToString();
                }
            }
            return dic;
        }
    }
}
