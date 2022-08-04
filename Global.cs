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

    }
}
