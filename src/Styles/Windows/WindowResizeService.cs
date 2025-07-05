using System;
using System.Runtime.InteropServices;
using System.Windows;
using Xaml.Effects.Toolkit.Uitity;
using static Xaml.Effects.Toolkit.Uitity.Win32Api;

namespace Xaml.Effects.Toolkit.Styles.Windows
{
    [Flags]
    internal enum HITPOSITION
    {
        NONE = 0,
        LEFT = 1,
        TOP = 2,
        RIGHT = 4,
        BOTTOM = 8,


    }





    public class WindowResizeService : IService
    {
        public WindowResizeService()
        {
            mousePoint = new Point();
            ResizeThickness = 2;
        }

        private HITPOSITION ClickPostion;



        protected override IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Api.WM_NCHITTEST)
            {
                Win32Api.RECT rect = Win32Api.GetWindowRect(hwnd);
                this.mousePoint.X = (lParam.ToInt32() & 0xFFFF);
                this.mousePoint.Y = (lParam.ToInt32() >> 16);
                ////告诉系统你已经处理过该消息，不然设置为false
                //窗口左上角
                if (this.mousePoint.Y - rect.top <= this.agWidth && this.mousePoint.X - rect.left <= this.agWidth)
                {
                    this.ClickPostion = HITPOSITION.LEFT | HITPOSITION.TOP;

                    handled = true;
                    return new IntPtr((int)Win32Api.HitTest.HTTOPLEFT);
                }
                // 窗口左下角　　
                else if (rect.Height + rect.top - this.mousePoint.Y <= this.agWidth && this.mousePoint.X - rect.left <= this.agWidth)
                {
                    this.ClickPostion = HITPOSITION.LEFT | HITPOSITION.BOTTOM;

                    handled = true;
                    return new IntPtr((int)Win32Api.HitTest.HTBOTTOMLEFT);
                }
                // 窗口右上角
                else if (this.mousePoint.Y - rect.top <= this.agWidth && rect.Width + rect.left - this.mousePoint.X <= this.agWidth)
                {
                    this.ClickPostion = HITPOSITION.RIGHT | HITPOSITION.TOP;
                    handled = true;
                    return new IntPtr((int)Win32Api.HitTest.HTTOPRIGHT);
                }
                // 窗口右下角
                else if (rect.Width + rect.left - this.mousePoint.X <= this.agWidth && rect.Height + rect.top - this.mousePoint.Y <= this.agWidth)
                {
                    this.ClickPostion = HITPOSITION.RIGHT | HITPOSITION.BOTTOM;
                    handled = true;
                    return new IntPtr((int)Win32Api.HitTest.HTBOTTOMRIGHT);
                }
                // 窗口左侧
                else if (this.mousePoint.X - rect.left <= this.bThickness)
                {
                    handled = true;
                    this.ClickPostion = HITPOSITION.LEFT;
                    return new IntPtr((int)Win32Api.HitTest.HTLEFT);
                }
                // 窗口右侧
                else if (rect.Width + rect.left - this.mousePoint.X <= this.bThickness)
                {
                    handled = true;
                    this.ClickPostion = HITPOSITION.RIGHT;
                    return new IntPtr((int)Win32Api.HitTest.HTRIGHT);
                }
                // 窗口上方
                else if (this.mousePoint.Y - rect.top <= this.bThickness)
                {
                    handled = true;
                    this.ClickPostion = HITPOSITION.TOP;
                    return new IntPtr((int)Win32Api.HitTest.HTTOP);
                }
                // 窗口下方
                else if (rect.Height + rect.top - this.mousePoint.Y <= this.bThickness)
                {
                    handled = true;
                    this.ClickPostion = HITPOSITION.BOTTOM;
                    return new IntPtr((int)Win32Api.HitTest.HTBOTTOM);
                }
            }
            else if (msg == Win32Api.WM_WINDOWPOSCHANGING)
            {
                Win32Api.WINDOWPOS wp = (Win32Api.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Win32Api.WINDOWPOS));
                // 限制窗口的最小宽度和高度
                int minWidth = this.MinWidth;
                int minHeight = this.MinHeight;

                if (wp.width < minWidth)
                {
                    if ((this.ClickPostion & HITPOSITION.LEFT) == HITPOSITION.LEFT)
                    {
                        wp.x += wp.width - minWidth;
                    }
                    wp.width = minWidth;
                }
                if (wp.height < minHeight)
                {
                    if ((this.ClickPostion & HITPOSITION.TOP) == HITPOSITION.TOP)
                    {
                        wp.y += wp.height - minHeight;
                    }
                    wp.height = minHeight;
                }

                if ((Win32Api.SWP_NOSIZE & wp.flags) == wp.flags)
                {
                    wp.flags &= ~Win32Api.SWP_NOSIZE;
                }


                Marshal.StructureToPtr(wp, lParam, true);
            }
            if (msg == Win32Api.WM_GETMINMAXINFO)
            {
                MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
                // 在这里设置窗口的最大和最小跟踪大小
                mmi.ptMinTrackSize.x = this.MinWidth;
                mmi.ptMinTrackSize.y = this.MinHeight;
                Marshal.StructureToPtr(mmi, lParam, true);
            }
            return base.WindowProc(hwnd, msg, wParam, lParam, ref handled);
        }



        /// <summary>
        /// 拐角宽度
        /// </summary>
        private Int32 agWidth
        {

            get
            {
                return ResizeThickness * 2;
            }
        }

        /// <summary>
        /// 边框宽度
        /// </summary>
        private Int32 bThickness
        {

            get
            {
                return ResizeThickness;
            }
        }





        #region ResizeThickness

        public static readonly DependencyProperty ResizeThicknessProperty =
        DependencyProperty.Register("ResizeThickness", typeof(Int32), typeof(WindowResizeService),
        new PropertyMetadata(4, ResizeThickness_Changle));

        private static void ResizeThickness_Changle(DependencyObject s, DependencyPropertyChangedEventArgs e)
        {

        }
        /// <summary>
        /// 窗口调整区域厚度
        /// </summary>
        public Int32 ResizeThickness
        {
            get
            {
                return (Int32)this.GetValue(ResizeThicknessProperty);
            }
            set
            {
                this.SetValue(ResizeThicknessProperty, value);
            }
        }


        #endregion






        #region MinWidth

        public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register("MinWidth", typeof(Int32), typeof(WindowResizeService), new PropertyMetadata(0, MinWidth_Changle));

        private static void MinWidth_Changle(DependencyObject s, DependencyPropertyChangedEventArgs e)
        {

        }
        /// <summary>
        /// 窗口最小宽度
        /// </summary>
        public Int32 MinWidth
        {
            get
            {
                return (Int32)this.GetValue(MinWidthProperty);
            }
            set
            {
                this.SetValue(MinWidthProperty, value);
            }
        }


        #endregion

        #region MinHeight

        public static readonly DependencyProperty MinHeightProperty =
        DependencyProperty.Register("MinHeight", typeof(Int32), typeof(WindowResizeService), new PropertyMetadata(0, MinHeight_Changle));

        private static void MinHeight_Changle(DependencyObject s, DependencyPropertyChangedEventArgs e)
        {

        }
        /// <summary>
        /// 窗口最小高度
        /// </summary>
        public Int32 MinHeight
        {
            get
            {
                return (Int32)this.GetValue(MinHeightProperty);
            }
            set
            {
                this.SetValue(MinHeightProperty, value);
            }
        }


        #endregion






        private Point mousePoint;

    }
}
