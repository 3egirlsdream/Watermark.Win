using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Watermark.Win.Models;

namespace Watermark.Core.Models
{
    public static class ClientInstance
    {

        public static Action<WMCanvas, WMLogo, Dictionary<string, string>> SelectImageAction = (canvas, mLogo, ImagesBase64) =>
        {
            Microsoft.Win32.OpenFileDialog dialog = new()
            {
                DefaultExt = ".png",  // 设置默认类型
                Multiselect = false,                             // 设置可选格式
                Filter = @"图像文件(*.jpg,*.png)|*jpeg;*.jpg;*.png|JPEG(*.jpeg, *.jpg)|*.jpeg;*.jpg|PNG(*.png)|*.png"
            };
            // 打开选择框选择
            Nullable<bool> result = dialog.ShowDialog();
            if (result == true)
            {
                var p = dialog.FileName;
                var destFolder = Global.TemplatesFolder + canvas.ID;
                if (!System.IO.Directory.Exists(destFolder))
                {
                    System.IO.Directory.CreateDirectory(destFolder);
                }

                var name = p.Substring(p.LastIndexOf('\\') + 1);
                var destFile = destFolder + System.IO.Path.DirectorySeparatorChar + name;
                System.IO.File.Copy(p, destFile, true);
                mLogo.Path = name;
                Global.ImageFile2Base64(ImagesBase64, destFile, mLogo.ID);
            }
        };



        public static Action<List<string>> InitLocalFontsAction = (Fonts) =>
        {
            Fonts ??= [];
            Fonts.Clear();
            var fontPath = AppDomain.CurrentDomain.BaseDirectory + "fonts";
            if (Directory.Exists(fontPath))
            {
                var files = Directory.GetFiles(fontPath);
                foreach (var file in files)
                {
                    Fonts.Add(System.IO.Path.GetFileName(file));
                }
            }
        };


        public static Action<List<string>> ImportLocalFontAction = (Fonts) =>
        {
            var fontPath = AppDomain.CurrentDomain.BaseDirectory + "fonts";
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.DefaultExt = ".ttf";  // 设置默认类型
            dialog.Multiselect = false;                             // 设置可选格式
            dialog.Filter = @"字体文件(*.ttf,*.otf)|*ttf;*.otf";
            // 打开选择框选择
            Nullable<bool> result = dialog.ShowDialog();
            if (result == true)
            {
                var f = dialog.FileName;

                var file = new FileInfo(f);
                if (file.Exists)
                {
                    try
                    {
                        file.CopyTo(fontPath, true);
                    }
                    catch { }
                    finally
                    {
                        InitLocalFontsAction.Invoke(Fonts);
                    }
                }

            }
        };

        public static Action<WMCanvas, WMText, string> SelectLocalFontAction = (CurrentCanvas, mText, fontName) =>
        {
            var fontPath = AppDomain.CurrentDomain.BaseDirectory + "fonts" + System.IO.Path.DirectorySeparatorChar + fontName;
            var targetPath = Global.TemplatesFolder + CurrentCanvas.ID;
            var file = new FileInfo(fontName);
            if (file.Exists)
            {
                try
                {
                    file.CopyTo(targetPath, true);
                    mText.FontFamily = fontName;
                }
                catch { }
            }
        };
    }
}
