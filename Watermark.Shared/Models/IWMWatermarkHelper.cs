using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Watermark.Win.Models;

namespace Watermark.Shared.Models
{
	public interface IWMWatermarkHelper
	{
		public Task<byte[]> GenerationAsync(WMCanvas mainCanvas, WMZipedTemplate ziped, bool isPreview, bool designMode = false);
		public byte[] Generation(WMCanvas mainCanvas, WMZipedTemplate ziped, bool isPreview, bool designMode = false);
	}
}
