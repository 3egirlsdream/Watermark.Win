using Foundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UIKit;

namespace Watermark.Platforms.iOS
{
    public static class SavePictureService
    {
        public static void SavePicture(byte[] arr)
        {
            // Convert byte[] to NSData
            var nsData = NSData.FromArray(arr);

            // Load UIImage from NSData
            var image = UIImage.LoadFromData(nsData);

            // Save image to Photos Album

            image.SaveToPhotosAlbum((img, error) =>
            {
                if (error != null)
                {
                    // Handle error
                    Console.WriteLine($"Error saving image: {error.LocalizedDescription}");
                }
                else
                {
                    Console.WriteLine("Image saved successfully.");
                }
            });
        }
    }
}
