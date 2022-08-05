using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JointWatermark
{
    public class Global
    {
        public static string logo = "";
        public static string BasePath;
        public static string sourceImgUrl;
        public static string lastUrl;
        public static char SeparatorChar = System.IO.Path.DirectorySeparatorChar;
        public static string Path_temp;
        public static string Path_output;
        public static string Path_logo;
        public static Color color = Color.White;


        public static string mount { get; set; }
        public static string xy { get; set; }
        public static string date { get; set; }
        public static string deviceName { get; set; }

        public static string FontFamily { get; set; } = "FZXiJinLJW";
        public static string FontFamilyLight { get; set; } = "微软雅黑Light";

        public static string Http { get; set; } = "http://thankful.top:4396";

        public static dynamic InitExifInfo(string filePath, bool showBrand)
        {
            try
            {
                var right1 = "f/1.8 1/40 ISO 400";
                var right2 = "44°29′12\"E 33°23′46\"W";
                var left1 = "A7C";
                var left2 = "";
                var ex = new ExifInfo2();
                var rs = ex.GetImageInfo(Image.FromFile(filePath));

                if (!rs.ContainsKey("f") || !rs.ContainsKey("exposure")|| !rs.ContainsKey("ISO")|| !rs.ContainsKey("mm"))
                {
                    return new
                    {
                        left1,
                        left2,
                        right1,
                        right2
                    };
                }

                right1 = $"F/{rs["f"]} {rs["exposure"]} ISO{rs["ISO"]} {rs["mm"]}";
                if (showBrand)
                {
                    left1 = $"{rs["producer"]} {rs["model"]}";
                }
                else
                {
                    left1 = $"{rs["model"]}";
                }

                if (rs.TryGetValue("mount", out string val) && !string.IsNullOrEmpty(val))
                {
                    right2 = val;
                }

                if (rs.TryGetValue("date", out string d) && !string.IsNullOrEmpty(d))
                {
                    left2 = d;
                }

                return new
                {
                    left1,
                    left2,
                    right1,
                    right2
                };
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }


    }
}
