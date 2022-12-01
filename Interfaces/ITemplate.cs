using JointWatermark.Class;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WeakToys.Class
{
    public interface ITemplate
    {
        public ImageProperties Properties { get; set; }
        public Task<Image<Rgba32>> CreateWatermark();
        public Task<Image> MergeWatermark();
    }
}
