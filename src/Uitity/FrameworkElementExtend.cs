using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Xaml.Effects.Toolkit.Uitity
{
    /// <summary>
    /// FrameworkElement 扩展类
    /// </summary>
    public static class FrameworkElementExtend
    {

        static FrameworkElementExtend()
        {
            //初始化DPI 因为改变DPI是要注销的，所以这里获取一次就够了
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                SystemDpiX = graphics.DpiX;
                SystemDpiY = graphics.DpiY;
            }
        }

        /// <summary>
        /// 获取对象集合内指定坐标处的对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="itemsControl"></param>
        /// <param name="point"></param>
        /// <returns></returns>
        public static T GetElementFromPoint<T>(this ItemsControl itemsControl, Point point)
            where T : FrameworkElement
        {
            UIElement element = itemsControl.InputHitTest(point) as UIElement;
            while (element != null)
            {
                if (element == itemsControl)
                    return default(T);
                object item = itemsControl.ItemContainerGenerator.ItemFromContainer(element);
                if (!item.Equals(DependencyProperty.UnsetValue))
                    return item as T;
                element = (UIElement)VisualTreeHelper.GetParent(element);
            }
            return default(T);
        }

        /// <summary>
        /// 获取对象集合内指定坐标处的对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="itemsControl"></param>
        /// <param name="point"></param>
        /// <returns></returns>
        public static T GetElementFromPoint<T>(this TreeView itemsControl, Point point)
            where T : FrameworkElement
        {
            UIElement element = itemsControl.InputHitTest(point) as UIElement;
            while (element != null)
            {
                if (element == itemsControl)
                    return default(T);
                object item = itemsControl.ItemContainerGenerator.ItemFromContainer(element);
                if (!item.Equals(DependencyProperty.UnsetValue))
                    return item as T;
                if (element is T)
                {
                    return element as T;
                }
                element = (UIElement)VisualTreeHelper.GetParent(element);
            }
            return default(T);
        }

        /// <summary>
        /// 获取控件元素在窗口的实际位置
        /// </summary>
        /// <param name="Control"></param>
        /// <returns></returns>
        public static Point GetElementLocation(this FrameworkElement Control)
        {
            Point location = new Point(0, 0);
            FrameworkElement element = Control;
            while (element != null)
            {
                var Offset = VisualTreeHelper.GetOffset(element);
                location = location + Offset;
                element = (FrameworkElement)VisualTreeHelper.GetParent(element);
            }
            return location;
        }

        /// <summary>
        /// 获取控件元素在指定对象的实际位置
        /// </summary>
        /// <param name="Control"></param>
        /// <param name="parentName">父控件的名称</param>
        /// <returns></returns>
        public static Point GetElementLocation(this FrameworkElement Control, String parentName)
        {
            Point location = new Point(0, 0);
            FrameworkElement element = Control;
            while (element != null)
            {
                if (element.Name == parentName)
                {
                    break;
                }
                var Offset = VisualTreeHelper.GetOffset(element);
                location = location + Offset;
                element = (FrameworkElement)VisualTreeHelper.GetParent(element);
            }
            return location;
        }

        /// <summary>
        /// 获取控件元素在指定对象的实际位置
        /// </summary>
        /// <param name="Control"></param>
        /// <param name="parent">父对象</param>
        /// <returns></returns>
        public static Point GetElementLocation(this FrameworkElement Control, FrameworkElement parent)
        {
            Point location = new Point(0, 0);
            FrameworkElement element = Control;
            while (element != null)
            {
                if (element == parent)
                {
                    break;
                }
                var Offset = VisualTreeHelper.GetOffset(element);
                location = location + Offset;
                element = (FrameworkElement)VisualTreeHelper.GetParent(element);
            }
            return location;
        }

        /// <summary>
        /// 获取控件元素在指定对象的实际位置
        /// </summary>
        /// <param name="Control"></param>
        /// <param name="parenttype">父对象类型</param>
        /// <returns></returns>
        public static Point GetElementLocation(this FrameworkElement Control, Type parenttype)
        {
            Point location = new Point(0, 0);
            FrameworkElement element = Control;
            while (element != null)
            {
                if (element.GetType() == parenttype)
                {
                    break;
                }
                var Offset = VisualTreeHelper.GetOffset(element);
                location = location + Offset;
                element = (FrameworkElement)VisualTreeHelper.GetParent(element);
            }
            return location;
        }

        /// <summary>
        /// 根据型查找父级对象
        /// </summary>
        /// <param name="Control"></param>
        /// <returns></returns>
        public static T FindParent<T>(this FrameworkElement Control)
            where T : FrameworkElement
        {
            FrameworkElement element = Control;
            while (element != null)
            {
                if (element.GetType() == typeof(T))
                {
                    return (T)element;
                }
                element = (FrameworkElement)VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        /// <summary>
        /// 根据提供的名称 查找父级控件
        /// </summary>
        /// <param name="Control"></param>
        /// <param name="parenttype">父对象类型</param>
        /// <returns></returns>
        public static FrameworkElement FindParent(this FrameworkElement Control, String parentName)
        {
            FrameworkElement element = Control;
            while (element != null)
            {
                if (element.Name == parentName)
                {
                    return element;
                }
                element = (FrameworkElement)VisualTreeHelper.GetParent(element);
            }
            return null;
        }


        /// <summary>
        /// 根据提供的名称 查找父级控件
        /// </summary>
        /// <param name="Control"></param>
        /// <param name="parenttype">父对象类型</param>
        /// <returns></returns>
        public static List<TResult> FindParents<TResult, TEnd>(this FrameworkElement Control)
            where TResult : FrameworkElement
        {
            List<TResult> results = new List<TResult>();
            FrameworkElement element = (FrameworkElement)VisualTreeHelper.GetParent(Control);
            while (element != null)
            {
                if (element.GetType() == typeof(TResult))
                {
                    results.Add((TResult)element);
                }
                else if (element.GetType() == typeof(TEnd))
                {
                    return results;
                }
                element = (FrameworkElement)VisualTreeHelper.GetParent(element);
            }
            return results;
        }




        /// <summary>
        /// 根据型查找子对象
        /// </summary>
        /// <param name="Control"></param>
        /// <returns></returns>
        public static List<T> FindChilds<T>(this FrameworkElement Control)
            where T : FrameworkElement
        {
            List<T> result = new List<T>();
            var childtotal = VisualTreeHelper.GetChildrenCount(Control);
            for (int i = 0; i < childtotal; i++)
            {
                FrameworkElement element = (FrameworkElement)VisualTreeHelper.GetChild(Control, i);
                if (element != null)
                {
                    if (element.GetType() == typeof(T))
                    {
                        result.Add((T)element);
                    }
                    var childs = element.FindChilds<T>();
                    if (childs.Count > 0)
                    {
                        result.AddRange(childs);
                    }
                }
            }
            return result;
        }




        /// <summary>
        /// 根据型查找子对象
        /// </summary>
        /// <param name="Control"></param>
        /// <returns></returns>
        public static T FindChild<T>(this FrameworkElement Control)
            where T : FrameworkElement
        {

            if (Control is ContentControl Ctl)
            {
                if (Ctl.Content.GetType() == typeof(T))
                {
                    return (T)Ctl.Content;
                }
                else if (Ctl.Content is FrameworkElement content)
                {
                    return content.FindChild<T>();
                }
                else
                {
                    return null;
                }
            }
            else
            {
                var childtotal = VisualTreeHelper.GetChildrenCount(Control);
                for (int i = 0; i < childtotal; i++)
                {
                    FrameworkElement element = (FrameworkElement)VisualTreeHelper.GetChild(Control, i);
                    if (element != null)
                    {
                        if (element.GetType() == typeof(T))
                        {
                            return (T)element;
                        }
                        else
                        {
                            var target = element.FindChild<T>();
                            if (target != null)
                            {
                                return target;
                            }
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 根据提供的名称 查找子控件
        /// </summary>
        /// <param name="Control"></param>
        /// <param name="parenttype">父对象类型</param>
        /// <returns></returns>
        public static FrameworkElement FindChild(this FrameworkElement Control, String childName)
        {
            if (Control is ContentControl Ctl)
            {
                if (Ctl.Content is FrameworkElement content)
                {
                    if (content.Name == childName)
                    {
                        return content;
                    }
                    return content.FindChild(childName);
                }
                else
                {
                    return null;
                }
            }
            else
            {
                var childtotal = VisualTreeHelper.GetChildrenCount(Control);
                for (int i = 0; i < childtotal; i++)
                {
                    FrameworkElement element = (FrameworkElement)VisualTreeHelper.GetChild(Control, i);
                    if (element != null)
                    {
                        if (element.Name == childName)
                        {
                            return element;
                        }
                        else
                        {
                            var target = element.FindChild(childName);
                            if (target != null)
                            {
                                return target;
                            }
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 获取父控件
        /// </summary>
        /// <param name="Control"></param>
        /// <param name="parenttype">父对象类型</param>
        /// <returns></returns>
        public static FrameworkElement GetParent(this FrameworkElement Control)
        {
            return (FrameworkElement)VisualTreeHelper.GetParent(Control);
        }

        /// <summary>
        /// 控件是否处于设计模式中
        /// </summary>
        /// <param name="control"></param>
        /// <returns></returns>
        public static bool IsDesignMode(this Control control)
        {
            return System.ComponentModel.DesignerProperties.GetIsInDesignMode(control);
        }

        /// <summary>
        /// 保存对象截图到文件
        /// </summary>
        /// <param name="ui"></param>
        /// <param name="fileName"></param>
        /// <param name="pixelFormat"></param>
        public static void SaveToImage(this FrameworkElement ui, String fileName, PixelFormat pixelFormat)
        {
            var source = ToImageSource(ui, pixelFormat);
            using (System.IO.FileStream fs = new System.IO.FileStream(fileName, System.IO.FileMode.Create))
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(fs);
            }
            ;
        }

        /// <summary>
        /// 保存对象截图到ImageSource
        /// </summary>
        /// <param name="ui"></param>
        /// <param name="fileName"></param>
        /// <param name="pixelFormat"></param>
        public static RenderTargetBitmap ToImageSource(this FrameworkElement ui, PixelFormat pixelFormat)
        {
            DrawingVisual drawingVisual = new DrawingVisual();
            //drawingVisual 的作用是校正Margin 和 Left Top
            using (DrawingContext context = drawingVisual.RenderOpen())
            {
                VisualBrush brush = new VisualBrush(ui) { Stretch = Stretch.None };
                context.DrawRectangle(brush, null, new Rect(0, 0, ui.ActualWidth, ui.ActualHeight));
                context.Close();
            }
            //使用drawingVisual渲染
            RenderTargetBitmap bmp = new RenderTargetBitmap((Int32)ui.ActualWidth, (Int32)ui.ActualHeight, SystemDpiX, SystemDpiY, pixelFormat);
            bmp.Render(drawingVisual);
            return bmp;
        }

        /// <summary>
        /// 保存对象截图到Bitmap
        /// </summary>
        /// <param name="ui"></param>
        /// <param name="fileName"></param>
        /// <param name="pixelFormat"></param>
        /// <returns></returns>
        public static System.Drawing.Bitmap ToBitmap(this FrameworkElement ui, PixelFormat pixelFormat)
        {
            var source = ToImageSource(ui, pixelFormat);
            using (var ms = new MemoryStream())
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(ms);
                return new System.Drawing.Bitmap(ms);
            }
        }

        public static double DpiScaleX
        {
            get
            {
                if (SystemDpiX != 96)
                {
                    return (double)SystemDpiX / 96.0;
                }
                return 1.0;
            }
        }

        public static double DpiScaleY
        {
            get
            {
                if (SystemDpiY != 96)
                {
                    return (double)SystemDpiY / 96.0;
                }
                return 1.0;
            }
        }

        //public static void GetDPI(out int dpix, out int dpiy)
        //{
        //    dpix = 0;
        //    dpiy = 0;
        //    using (System.Management.ManagementClass mc = new System.Management.ManagementClass("Win32_DesktopMonitor"))
        //    {
        //        using (System.Management.ManagementObjectCollection moc = mc.GetInstances())
        //        {

        //            foreach (System.Management.ManagementObject each in moc)
        //            {
        //                dpix = int.Parse((each.Properties["PixelsPerXLogicalInch"].Value.ToString()));
        //                dpiy = int.Parse((each.Properties["PixelsPerYLogicalInch"].Value.ToString()));
        //            }
        //        }
        //    }
        //}






        public static Single SystemDpiX { get; private set; }
        public static Single SystemDpiY { get; private set; }






    }
}
