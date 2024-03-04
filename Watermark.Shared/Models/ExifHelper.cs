using ExifLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watermark.Win.Models
{
    public static class ExifHelper
    {
        static string s = "{\"ImageDescription\":\"  \",\"Make\":\"SONY\",\"Model\":\"ILCE-7C\",\"XResolution\":\"300\",\"YResolution\":\"300\",\"ResolutionUnit\":\"2\",\"Software\":\"Capture One 22 Windows\",\"ExposureTime\":\"1/250\",\"FNumber\":\"4\",\"ExposureProgram\":\"3\",\"ISOSpeedRatings\":\"100\",\"PhotographicSensitivity\":\"100\",\"SensitivityType\":\"2\",\"RecommendedExposureIndex\":\"100\",\"ExifVersion\":\"\",\"DateTimeOriginal\":\"2022:11:13 11:15:45\",\"DateTimeOriginalgz\":\"癸丑年腊月十二日\",\"DateTimeDigitized\":\"2022:11:13 11:15:45\",\"DateTimeDigitizedgz\":\"癸丑年腊月十二\",\"ShutterSpeedValue\":\"11.287712\",\"ApertureValue\":\"4\",\"BrightnessValue\":\"11.2125\",\"ExposureBiasValue\":\"0\",\"MaxApertureValue\":\"4\",\"MeteringMode\":\"5\",\"LightSource\":\"0\",\"Flash\":\"16\",\"FocalLength\":\"105\",\"SubsecTimeOriginal\":\"841\",\"SubsecTimeDigitized\":\"841\",\"PixelXDimension\":\"4010\",\"PixelYDimension\":\"2673\",\"FileSource\":\"3\",\"SceneType\":\"1\",\"CustomRendered\":\"0\",\"ExposureMode\":\"0\",\"WhiteBalance\":\"0\",\"DigitalZoomRatio\":\"1\",\"FocalLengthIn35mmFilm\":\"105\",\"SceneCaptureType\":\"0\",\"Contrast\":\"0\",\"Saturation\":\"0\",\"Sharpness\":\"0\",\"LensSpecification\":\"\",\"LensModel\":\"Sony 50.4GM\"}";
        static string keyvalue = "{\r\n  \"ImageDescription\": \"图片描述\",\r\n  \"Make\": \"制造商\",\r\n  \"Model\": \"型号\",\r\n  \"XResolution\": \"水平分辨率\",\r\n  \"YResolution\": \"垂直分辨率\",\r\n  \"ResolutionUnit\": \"分辨率单位\",\r\n  \"Software\": \"软件\",\r\n  \"ExposureTime\": \"曝光时间\",\r\n  \"FNumber\": \"光圈值\",\r\n  \"ExposureProgram\": \"曝光程序\",\r\n  \"ISOSpeedRatings\": \"ISO感光度\",\r\n  \"PhotographicSensitivity\": \"摄影感光度\",\r\n  \"SensitivityType\": \"感光度类型\",\r\n  \"RecommendedExposureIndex\": \"推荐曝光指数\",\r\n  \"ExifVersion\": \"Exif版本\",\r\n  \"DateTimeOriginal\": \"拍摄时间\",\r\n  \"DateTimeOriginalgz\": \"拍摄时间(干支)\",\r\n  \"DateTimeDigitized\": \"数字化时间\",\r\n  \"DateTimeDigitizedgz\": \"数字化时间(干支)\",\r\n  \"ShutterSpeedValue\": \"快门速度值\",\r\n  \"ApertureValue\": \"光圈值\",\r\n  \"BrightnessValue\": \"亮度值\",\r\n  \"ExposureBiasValue\": \"曝光补偿值\",\r\n  \"MaxApertureValue\": \"最大光圈值\",\r\n  \"MeteringMode\": \"测光模式\",\r\n  \"LightSource\": \"光源\",\r\n  \"Flash\": \"闪光灯\",\r\n  \"FocalLength\": \"焦距\",\r\n  \"SubsecTimeOriginal\": \"原始子秒\",\r\n  \"SubsecTimeDigitized\": \"数字化子秒\",\r\n  \"PixelXDimension\": \"水平像素\",\r\n  \"PixelYDimension\": \"垂直像素\",\r\n  \"FileSource\": \"文件来源\",\r\n  \"SceneType\": \"场景类型\",\r\n  \"CustomRendered\": \"自定义渲染\",\r\n  \"ExposureMode\": \"曝光模式\",\r\n  \"WhiteBalance\": \"白平衡\",\r\n  \"DigitalZoomRatio\": \"数字变焦比例\",\r\n  \"FocalLengthIn35mmFilm\": \"35mm胶片等效焦距\",\r\n  \"SceneCaptureType\": \"场景捕捉类型\",\r\n  \"Contrast\": \"对比度\",\r\n  \"Saturation\": \"饱和度\",\r\n  \"Sharpness\": \"锐度\",\r\n  \"LensSpecification\": \"镜头规格\",\r\n  \"LensModel\": \"镜头型号\"\r\n}";
        private static Dictionary<string, string> defaultMeta;
        public static Dictionary<string, string> GetName
        {
            get
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(keyvalue) ?? [];
            }
        }

        public static Dictionary<string, string> DefaultMeta
        {
            get
            {
                return defaultMeta ??= JsonConvert.DeserializeObject<Dictionary<string, string>>(s) ?? [];
            }
        }
        public static Dictionary<string, string> ReadImage(string path)
        {
            try
            {
                if(!File.Exists(path))
                {
                    return DefaultMeta;
                }
                var exifReader = new ExifReader(path);
                var dic = new Dictionary<string, string>();
                foreach (var key in Enum.GetNames(typeof(ExifTags)))
                {
                    var e = (ExifTags)Enum.Parse(typeof(ExifTags), key);
                    if (exifReader.GetTagValue(e, out object result))
                    {
                        dic[key] = result.ToString();
                        if (result is byte[] v) dic[key] = Convert.ToBase64String(v);
                        else if (key == Enum.GetName(ExifTags.ExposureTime) && result is double et)
                        {
                            dic[key] = "1/" + (1 / et);
                        }
                        else if (result is DateTime || key.Contains("DateTime"))
                        {
                            DateTime dt;
                            if(result is DateTime) 
                            { 
                                dt = (DateTime)result; 
                            } 
                            else
                            {
                                string format = "yyyy:MM:dd HH:mm:ss";
                                DateTime.TryParseExact(result.ToString(), format, null, System.Globalization.DateTimeStyles.None, out dt);
                            }
                            var k = key + "gz";
                            var ganzhi = Global.GetGanZhi(dt.Year);
                            var mon = Global.GetMonth(dt.Month);
                            var day = Global.GetDay(dt.Day);
                            dic[k] = ganzhi + "年" + mon + "月" + day + "日";
                        }
                    }
                }
                return dic;
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return DefaultMeta;
            }
        }

        public static Dictionary<string, string> ReadImage(byte[] path)
        {
            var stream = new MemoryStream(path);
            return ReadImage(stream);
        }

        public static Dictionary<string, string> ReadImage(Stream path)
        {
            try
            {
                var exifReader = new ExifReader(path);
                var dic = new Dictionary<string, string>();
                foreach (var key in Enum.GetNames(typeof(ExifTags)))
                {
                    var e = (ExifTags)Enum.Parse(typeof(ExifTags), key);
                    if (exifReader.GetTagValue(e, out object result))
                    {
                        dic[key] = result.ToString();
                        if (result is byte[] v) dic[key] = Convert.ToBase64String(v);
                        else if (key == Enum.GetName(ExifTags.ExposureTime) && result is double et)
                        {
                            dic[key] = "1/" + (1 / et);
                        }
                        else if (result is DateTime || key.Contains("DateTime"))
                        {
                            DateTime dt;
                            if (result is DateTime time)
                            {
                                dt = time;
                            }
                            else
                            {
                                DateTime.TryParse(result.ToString(), out dt);
                            }
                            var k = key + "gz";
                            var ganzhi = Global.GetGanZhi(dt.Year);
                            var mon = Global.GetMonth(dt.Month);
                            var day = Global.GetDay(dt.Day);
                            dic[k] = ganzhi + "年" + mon + "月" + day + "日";
                        }
                    }
                }
                return dic;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return DefaultMeta;
            }
        }

        public static Task<Dictionary<string, string>> ReadImageAsync(string path)
        {
            return Task.Run(() => { return ReadImage(path); });
        }
    }
}
