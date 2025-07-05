
using Assets.Editor.Common;
using Resource.Package.Assets;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Assets.Editor.Converter
{


    /// <summary>
    /// Boolean to visible converter
    /// </summary>
    public class BitmapSourceConverter : System.Windows.Markup.MarkupExtension, IMultiValueConverter
    {
        private BitmapSource Empty { get; set; }

        public BitmapSourceConverter()
        {
            this.Empty = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4] { 0, 0, 0, 0 }, 4);
        }

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Length == 0) return Empty;
            if (values[0] is BitmapSource source)
            {
                if (source.Width == 1 && source.Height == 1)
                {
                    return Empty;
                }
                var mode = DrawingMode.Raw;
                if (values.Length > 1 && values[1] is DrawingMode _mode) mode = _mode;
                if (mode == DrawingMode.Raw) return source;
                if (mode == DrawingMode.MaskColor)
                {
                    var color = System.Windows.Media.Color.FromArgb(255, 0, 0, 0);
                    if (values.Length > 2 && values[2] is System.Windows.Media.Color _color) color = _color;
                    return MaskColorFilter(source, color);
                }
                else if (mode == DrawingMode.AlphaBlend)
                {
                    return this.AlphaBlendFilter(source, 3);
                }
                else if (mode == DrawingMode.UnpremultiplyAlpha)
                {
                    return this.UnpremultiplyAlpha(source);
                }
                else
                {
                    return source;
                }
            }
            return null;
        }







        private BitmapSource MaskColorFilter(BitmapSource bitmap, System.Windows.Media.Color color)
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






        private BitmapSource UnpremultiplyAlpha(BitmapSource bitmap)
        {
            FormatConvertedBitmap fb = new FormatConvertedBitmap();
            fb.BeginInit();
            fb.Source = bitmap;
            fb.DestinationFormat = PixelFormats.Bgra32;
            fb.EndInit();
            var stride = (fb.PixelWidth * fb.Format.BitsPerPixel + 7) / 8;
            byte[] buf = new byte[fb.PixelHeight * stride];
            fb.CopyPixels(Int32Rect.Empty, buf, stride, 0);
            AlphaUtil.PremultiplyAlpha(buf);
            AlphaUtil.PremultiplyAlpha(buf);
            var source = new WriteableBitmap(fb.PixelWidth, fb.PixelHeight, fb.DpiX, fb.DpiY, fb.Format, fb.Palette);
            source.WritePixels(new Int32Rect(0, 0, fb.PixelWidth, fb.PixelHeight), buf, stride, 0);
            return source;// BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.DpiX, bitmap.DpiY, bitmap.Format, null, buf, stride);
        }





        private BitmapSource AlphaBlendFilter(BitmapSource bitmap, Byte thresholdvalue)
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





        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return null;
        }



        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }


    }
}