using Resource.Package.Assets.Common;
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Assets.Editor
{
    public static class BitmapUtil
    {
        public static readonly BitmapSource EmptyBitmapSource;

        public static readonly Byte[] EmptyBitmapData;


        static BitmapUtil()
        {
            EmptyBitmapSource = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4] { 255, 255, 255, 20 }, 4);
            using (MemoryStream ms = new MemoryStream())
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(EmptyBitmapSource));
                encoder.Save(ms);
                EmptyBitmapData = ms.ToArray();
            }
        }

        public static System.Drawing.Imaging.ImageFormat GetOutFileFormat(ImageTypes type)
        {
            if (type == ImageTypes.GIF) return System.Drawing.Imaging.ImageFormat.Gif;
            if (type == ImageTypes.TIFF) return System.Drawing.Imaging.ImageFormat.Tiff;
            if (type == ImageTypes.BMP) return System.Drawing.Imaging.ImageFormat.Bmp;
            if (type == ImageTypes.TGA) return System.Drawing.Imaging.ImageFormat.Png;
            if (type == ImageTypes.JPG) return System.Drawing.Imaging.ImageFormat.Jpeg;
            if (type == ImageTypes.PNG) return System.Drawing.Imaging.ImageFormat.Png;
            return System.Drawing.Imaging.ImageFormat.Png;
        }


        public static ImageTypes ParseImageFormat(Byte[] data)
        {
            if (data.Length == 0) return ImageTypes.Unknown;
            if (data[0] == 0x42 && data[1] == 0x4D) return ImageTypes.BMP;
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return ImageTypes.PNG;
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ImageTypes.JPG;
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38) return ImageTypes.GIF;
            if (data[0] == 0x00 && data[1] == 0x00 && (data[2] == 0x02 || data[2] == 0x0A)) return ImageTypes.TGA;
            if (data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00) return ImageTypes.TIFF;
            return ImageTypes.Unknown;
        }
        public static void SavePng(this BitmapSource source, String filePath)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(fileStream);
            }
        }


        public static void FromBase64Transform(String fileName)
        {
            FileStream fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = fileStream;
            img.EndInit();
        }





    }
}
