using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Watermark.Win.Models
{
    public static class Global
    {
        public static string TemplatesFolder = AppDomain.CurrentDomain.BaseDirectory + "Templates" + System.IO.Path.DirectorySeparatorChar;

        public static WMCanvas ReadConfig(string s)
        {
            try
            {
                var ls = Newtonsoft.Json.JsonConvert.DeserializeObject<WMCanvasSerialize>(s);
                var newCanvas = new WMCanvas()
                {
                    ID = ls.ID,
                    Name = ls.Name,
                    BorderThickness = ls.BorderThickness,
                    BackgroundColor=  ls.BackgroundColor,
                    EnableMarginXS = ls.EnableMarginXS
                };
                //一级容器
                newCanvas.Children = new List<WMContainer>(ls.Containers.Where(c => c.PNode.PID == "0"));
                //每个容器下的二级节点
                foreach (var container in newCanvas.Children)
                {
                    if (container.Controls == null) container.Controls = new List<IWMControl>();
                    container.Controls.AddRange(ls.Lines.Where(c => c.PNode.PID == container.ID));
                    container.Controls.AddRange(ls.Logos.Where(c => c.PNode.PID == container.ID));
                    container.Controls.AddRange(ls.Texts.Where(c => c.PNode.PID == container.ID));

                    var secondContainer = ls.Containers.Where(c => c.PNode.PID == container.ID);
                    foreach (var ctrl in secondContainer)
                    {
                        if (ctrl.Controls == null) ctrl.Controls = new List<IWMControl>();
                        ctrl.Controls.AddRange(ls.Lines.Where(c => c.PNode.PID == ctrl.ID));
                        ctrl.Controls.AddRange(ls.Logos.Where(c => c.PNode.PID == ctrl.ID));
                        ctrl.Controls.AddRange(ls.Texts.Where(c => c.PNode.PID == ctrl.ID));
                    }
                    secondContainer = secondContainer.OrderBy(c => c.PNode.SEQ);
                    container.Controls.AddRange(secondContainer);
                    container.Controls = new List<IWMControl>(container.Controls.OrderBy(c => c.PNode.SEQ));
                }
                newCanvas.Children = new List<WMContainer>(newCanvas.Children.OrderBy(c => c.PNode.SEQ));
                return newCanvas;
            }
            catch(Exception ex)
            {
                return new WMCanvas();
            }
        }

        public static string CanvasSerialize(WMCanvas canvas)
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                MissingMemberHandling = MissingMemberHandling.Ignore // 忽略缺少的字段
            };
            var nc = JsonConvert.DeserializeObject<WMCanvasSerialize>(JsonConvert.SerializeObject(canvas), settings);

            nc.Containers = new List<WMContainer>();
            nc.Lines = new List<WMLine>();
            nc.Logos = new List<WMLogo>();
            nc.Texts = new List<WMText>();


            for (var i = 0; i < canvas.Children.Count; i++)
            {
                var ct = canvas.Children[i];
                ct.PNode = new PNode(i, "0");
                for (var j = 0; j < ct.Controls.Count; j++)
                {
                    var mc = ct.Controls[j];
                    mc.PNode = new PNode(j, ct.ID);
                    if (mc is WMLine mLine) nc.Lines.Add(mLine);
                    else if (mc is WMLogo mLogo) nc.Logos.Add(mLogo);
                    else if (mc is WMText mText) nc.Texts.Add(mText);
                    else if (mc is WMContainer mContainer)
                    {
                        for (var k = 0; k < mContainer.Controls.Count; k++)
                        {
                            var child = mContainer.Controls[k];
                            child.PNode = new PNode(k, mContainer.ID);
                            if (child is WMLine line) nc.Lines.Add(line);
                            else if (child is WMLogo logo) nc.Logos.Add(logo);
                            else if (child is WMText text) nc.Texts.Add(text);
                        }
                        var cld_copy = JsonConvert.DeserializeObject<WMContainer>(JsonConvert.SerializeObject(mContainer));
                        cld_copy.Controls = [];
                        nc.Containers.Add(cld_copy);
                    }
                }
                var copy = JsonConvert.DeserializeObject<WMContainer>(JsonConvert.SerializeObject(ct));
                copy.Controls = new List<IWMControl>();
                nc.Containers.Add(copy);
            }

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(nc, Newtonsoft.Json.Formatting.Indented);
            return json;
        }

        public static void ImageFile2Base64(Dictionary<string, string> ImagesBase64, string destFile, string id)
        {
            using var bitmap = SkiaSharp.SKBitmap.Decode(destFile);
            if (bitmap != null)
            {
                using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 50);
                ImagesBase64[id] = "data:image/jpeg;base64," + Convert.ToBase64String(data.ToArray());
            }
            else
            {
                ImagesBase64[id] = "";
            }
        }
    }
}
