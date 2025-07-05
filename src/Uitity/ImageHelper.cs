using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Xaml.Effects.Toolkit.Uitity
{
    public static class ImageHelper
    {


        /// <summary>
        /// 从文件加载图片
        /// </summary>
        /// <param name="FileName"></param>
        /// <returns></returns>
        public static BitmapImage LoadImage(String FileName)
        {
            BitmapImage bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(FileName);
            bi.EndInit();
            return bi;
        }



        /// <summary>
        /// 转换为灰度图
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public static ImageSource ConvertGray(BitmapSource i)
        {
            if (i == null)
                return null;
            FormatConvertedBitmap b = new FormatConvertedBitmap();
            b.BeginInit();
            b.Source = i;
            b.DestinationFormat = PixelFormats.Gray8;
            b.EndInit();
            return b;
        }


        /// <summary>
        /// 为图片生成缩略图  
        /// </summary>
        /// <param name="phyPath">原图片的路径</param>
        /// <param name="width">缩略图宽</param>
        /// <param name="height">缩略图高</param>
        /// <returns></returns>
        public static System.Drawing.Image GetThumbnail(this System.Drawing.Image image, int width, int height)
        {
            System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(width, height);
            //从Bitmap创建一个System.Drawing.Graphics
            System.Drawing.Graphics gr = System.Drawing.Graphics.FromImage(bmp);
            //设置 
            gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            //下面这个也设成高质量
            gr.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            //下面这个设成High
            gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            //把原始图像绘制成上面所设置宽高的缩小图
            System.Drawing.Rectangle rectDestination = new System.Drawing.Rectangle(0, 0, width, height);

            gr.DrawImage(image, rectDestination, 0, 0, image.Width, image.Height, System.Drawing.GraphicsUnit.Pixel);
            return bmp;
        }





        /// <summary>
        /// 获取两个颜色之间的颜色
        /// </summary>
        /// <param name="Source">颜色1</param>
        /// <param name="Destinat">颜色2</param>
        /// <param name="_bf">百分比</param>
        /// <returns></returns>
        public static Color GetCenterColor(Color Source, Color Destinat, Double _bf)
        {
            Double d_bf = _bf / 100;
            Double A = Source.A + (Destinat.A - Source.A) * d_bf;
            Double R = Source.R + (Destinat.R - Source.R) * d_bf;
            Double G = Source.G + (Destinat.G - Source.G) * d_bf;
            Double B = Source.B + (Destinat.B - Source.B) * d_bf;
            Color nColor = Color.FromArgb((Byte)A, (Byte)R, (Byte)G, (Byte)B);
            return nColor;
        }






    }
}
