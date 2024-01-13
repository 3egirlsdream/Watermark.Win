using ExifLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watermark.Win.Models
{
    public static class ExifHelper
    {
        static string s = "{\"ImageDescription\":\"  \",\"Make\":\"SONY\",\"Model\":\"ILCE-7C\",\"XResolution\":\"300\",\"YResolution\":\"300\",\"ResolutionUnit\":\"2\",\"Software\":\"Capture One 22 Windows\",\"ExposureTime\":\"0.0004\",\"FNumber\":\"4\",\"ExposureProgram\":\"3\",\"ISOSpeedRatings\":\"100\",\"PhotographicSensitivity\":\"100\",\"SensitivityType\":\"2\",\"RecommendedExposureIndex\":\"100\",\"ExifVersion\":\"System.Byte[]\",\"DateTimeOriginal\":\"2022:11:13 11:15:45\",\"DateTimeDigitized\":\"2022:11:13 11:15:45\",\"ShutterSpeedValue\":\"11.287712\",\"ApertureValue\":\"4\",\"BrightnessValue\":\"11.2125\",\"ExposureBiasValue\":\"0\",\"MaxApertureValue\":\"4\",\"MeteringMode\":\"5\",\"LightSource\":\"0\",\"Flash\":\"16\",\"FocalLength\":\"105\",\"SubsecTimeOriginal\":\"841\",\"SubsecTimeDigitized\":\"841\",\"PixelXDimension\":\"4010\",\"PixelYDimension\":\"2673\",\"FileSource\":\"3\",\"SceneType\":\"1\",\"CustomRendered\":\"0\",\"ExposureMode\":\"0\",\"WhiteBalance\":\"0\",\"DigitalZoomRatio\":\"1\",\"FocalLengthIn35mmFilm\":\"105\",\"SceneCaptureType\":\"0\",\"Contrast\":\"0\",\"Saturation\":\"0\",\"Sharpness\":\"0\",\"LensSpecification\":\"System.Double[]\",\"LensModel\":\"Sony FE 24-105mm F4 G OSS (SEL24105G)\"}";
        public static Dictionary<string, string> ReadImage(string path)
        {
            try
            {
                ExifReader exifReader = new ExifReader(path);
                var dic = new Dictionary<string, string>();
                foreach (var key in Enum.GetNames(typeof(ExifTags)))
                {
                    var e = (ExifTags)Enum.Parse(typeof(ExifTags), key);
                    if (exifReader.GetTagValue(e, out object result))
                    {
                        dic[key] = result.ToString();
                    }
                }
                return dic;
            }
            catch
            {
                var dic = JsonConvert.DeserializeObject<Dictionary<string, string>>(s);
                return dic;
            }
        }
    }
}
