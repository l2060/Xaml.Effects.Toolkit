using Assets.Editor.Common;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Assets.Editor.Utils
{
    public class LengendImageUtil
    {

        public static Bitmap Convert(Bitmap bitmap, DrawingMode mode)
        {
            if (bitmap.Width == 1 && bitmap.Height == 1) return ClearColorFilter(bitmap);
            if (mode == DrawingMode.AlphaBlend) return AlphaBlendFilter(bitmap, 2);
            if (mode == DrawingMode.MaskColor) return MaskColorFilter(bitmap, new System.Windows.Media.Color());
            return bitmap;
        }


        public static unsafe Bitmap ClearColorFilter(Bitmap mybm)
        {
            var lpdata = mybm.LockBits(new Rectangle(new System.Drawing.Point(0, 0), mybm.Size), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var Pixels = new byte[(mybm.Width * mybm.Height) * 4];
            Marshal.Copy(lpdata.Scan0, Pixels, 0, Pixels.Length);
            fixed (Byte* p = &Pixels[0])
            {
                for (int i = 0; i < Pixels.Length; i += 4)
                {
                    p[i + 3] = 0;
                    p[i + 2] = 0;
                    p[i + 1] = 0;
                    p[i] = 0;
                }
            }
            mybm.UnlockBits(lpdata);
            Bitmap bm = new Bitmap(mybm.Width, mybm.Height);
            lpdata = bm.LockBits(new Rectangle(new System.Drawing.Point(0, 0), mybm.Size), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Marshal.Copy(Pixels, 0, lpdata.Scan0, Pixels.Length);
            bm.UnlockBits(lpdata);
            return bm;
        }


        public static unsafe Bitmap MaskColorFilter(Bitmap mybm, System.Windows.Media.Color color)
        {
            var lpdata = mybm.LockBits(new Rectangle(new System.Drawing.Point(0, 0), mybm.Size), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var Pixels = new byte[(mybm.Width * mybm.Height) * 4];
            Marshal.Copy(lpdata.Scan0, Pixels, 0, Pixels.Length);
            fixed (Byte* p = &Pixels[0])
            {
                for (int i = 0; i < Pixels.Length; i += 4)
                {
                    var b = color.R == p[i + 2] && color.G == p[i + 1] && color.B == p[i];
                    p[i + 3] = b ? (Byte)0 : (Byte)255;
                }
            }
            mybm.UnlockBits(lpdata);
            Bitmap bm = new Bitmap(mybm.Width, mybm.Height);
            lpdata = bm.LockBits(new Rectangle(new System.Drawing.Point(0, 0), mybm.Size), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Marshal.Copy(Pixels, 0, lpdata.Scan0, Pixels.Length);
            bm.UnlockBits(lpdata);
            return bm;
        }



        public static BitmapSource MaskColorFilter(BitmapSource bitmap, System.Windows.Media.Color color)
        {
            FormatConvertedBitmap fb = new FormatConvertedBitmap();
            fb.BeginInit();
            fb.Source = bitmap;
            fb.DestinationFormat = PixelFormats.Bgra32;
            fb.EndInit();
            var stride = (fb.PixelWidth * fb.Format.BitsPerPixel + 7) / 8;
            byte[] buf = new byte[fb.PixelHeight * stride];
            fb.CopyPixels(Int32Rect.Empty, buf, stride, 0);
            for (long ic = 0; ic < buf.LongLength; ic += 4)
            {
                if (buf[ic] == color.R && buf[ic + 1] == color.G && buf[ic + 2] == color.B && buf[ic + 3] == color.A)
                {
                    buf[ic] = 0x00;
                    buf[ic + 1] = 0x00;
                    buf[ic + 2] = 0x00;
                    buf[ic + 3] = 0x00;//透明处理
                }
            }
            var source = new WriteableBitmap(fb.PixelWidth, fb.PixelHeight, fb.DpiX, fb.DpiY, fb.Format, fb.Palette);
            source.WritePixels(new Int32Rect(0, 0, fb.PixelWidth, fb.PixelHeight), buf, stride, 0);
            return source;// BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.DpiX, bitmap.DpiY, bitmap.Format, null, buf, stride);
        }








        public static unsafe Bitmap AlphaBlendFilter(Bitmap mybm, Double thresholdvalue = 3.0)
        {
            var lpdata = mybm.LockBits(new Rectangle(new System.Drawing.Point(0, 0), mybm.Size), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var Pixels = new byte[(mybm.Width * mybm.Height) * 4];
            Marshal.Copy(lpdata.Scan0, Pixels, 0, Pixels.Length);
            fixed (Byte* p = &Pixels[0])
            {
                for (int i = 0; i < Pixels.Length; i += 4)
                {
                    float r = (float)p[i + 2] / 255.0f;
                    float g = (float)p[i + 1] / 255.0f;
                    float b = (float)p[i] / 255.0f;
                    float max = r, min = r;
                    if (g > max) max = g;
                    if (b > max) max = b;
                    if (g < min) min = g;
                    if (b < min) min = b;
                    var Brightness = (max + min) / 2;
                    var Alpha = (Int32)(Brightness * 255) * thresholdvalue;
                    p[i + 3] = (Byte)(Alpha > 255 ? 255 : Alpha);
                }
            }
            mybm.UnlockBits(lpdata);
            Bitmap bm = new Bitmap(mybm.Width, mybm.Height);
            lpdata = bm.LockBits(new Rectangle(new System.Drawing.Point(0, 0), mybm.Size), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Marshal.Copy(Pixels, 0, lpdata.Scan0, Pixels.Length);
            bm.UnlockBits(lpdata);
            return bm;
        }


        public static BitmapSource AlphaBlendFilter(BitmapSource bitmap, Byte thresholdvalue)
        {
            FormatConvertedBitmap fb = new FormatConvertedBitmap();
            fb.BeginInit();
            fb.Source = bitmap;
            fb.DestinationFormat = PixelFormats.Bgra32;
            fb.EndInit();
            var stride = (fb.PixelWidth * fb.Format.BitsPerPixel + 7) / 8;
            byte[] buf = new byte[fb.PixelHeight * stride];
            fb.CopyPixels(Int32Rect.Empty, buf, stride, 0);


            for (int i = 0; i < buf.LongLength; i += 4)
            {
                var B = buf[i];
                var G = buf[i + 1];
                var R = buf[i + 2];
                //var A = buf[i + 3];
                float r = (float)R / 255.0f;
                float g = (float)G / 255.0f;
                float b = (float)B / 255.0f;
                float max = r, min = r;
                if (g > max) max = g;
                if (b > max) max = b;
                if (g < min) min = g;
                if (b < min) min = b;
                var Brightness = (max + min) / 2;
                var Alpha = (Int32)(Brightness * 255) * thresholdvalue;
                buf[i + 3] = (Byte)(Alpha > 255 ? 255 : Alpha);
            }
            var source = new WriteableBitmap(fb.PixelWidth, fb.PixelHeight, fb.DpiX, fb.DpiY, fb.Format, fb.Palette);
            source.WritePixels(new Int32Rect(0, 0, fb.PixelWidth, fb.PixelHeight), buf, stride, 0);
            return source;// BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.DpiX, bitmap.DpiY, bitmap.Format, null, buf, stride);
        }

    }
}
