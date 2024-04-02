using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Watermark.Win.Models;

namespace Watermark.Shared.Models
{
	public class WMTemplateList
	{
		public string ID { get; set; }
		public string Path { get; set; }
		public WMCanvas Canvas { get; set; }
		public SkiaSharp.SKBitmap Bitmap { get; set; }
		public string Src { get; set; }
	}
}
