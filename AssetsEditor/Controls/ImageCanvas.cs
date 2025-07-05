
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xaml.Effects.Toolkit.Uitity;

namespace Assets.Editor.Controls
{
    public class ImageCanvas : Canvas
    {
        public ImageCanvas()
        {
            this.Focusable = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        }



        public Boolean ZeroLine
        {
            get
            {
                return (Boolean)GetValue(ZeroLineProperty);
            }
            set
            {
                SetValue(ZeroLineProperty, value);
            }
        }
        public static readonly DependencyProperty ZeroLineProperty = DependencyProperty.Register("ZeroLine", typeof(Boolean), typeof(ImageCanvas), new FrameworkPropertyMetadata(false, PropertyChangedCallback));



        public Boolean HumLine
        {
            get
            {
                return (Boolean)GetValue(HumLineProperty);
            }
            set
            {
                SetValue(HumLineProperty, value);
            }
        }
        public static readonly DependencyProperty HumLineProperty = DependencyProperty.Register("HumLine", typeof(Boolean), typeof(ImageCanvas), new FrameworkPropertyMetadata(false, PropertyChangedCallback));



        public Boolean BoundsLine
        {
            get
            {
                return (Boolean)GetValue(BoundsLineProperty);
            }
            set
            {
                SetValue(BoundsLineProperty, value);
            }
        }
        public static readonly DependencyProperty BoundsLineProperty = DependencyProperty.Register("BoundsLine", typeof(Boolean), typeof(ImageCanvas), new FrameworkPropertyMetadata(false, PropertyChangedCallback));



        public Rect CustomRect
        {
            get
            {
                return (Rect)GetValue(CustomRectProperty);
            }
            set
            {
                SetValue(CustomRectProperty, value);
            }
        }
        public static readonly DependencyProperty CustomRectProperty = DependencyProperty.Register("CustomRect", typeof(Rect), typeof(ImageCanvas), new FrameworkPropertyMetadata(Rect.Empty, PropertyChangedCallback));







        public ImageSource Source
        {
            get
            {
                return (ImageSource)GetValue(SourceProperty);
            }
            set
            {
                SetValue(SourceProperty, value);
            }
        }
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(ImageSource), typeof(ImageCanvas), new FrameworkPropertyMetadata(default(ImageSource), PropertyChangedCallback));


        public static void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as ImageCanvas).InvalidateVisual();
        }



        public Int16 OffsetX
        {
            get
            {
                return (Int16)GetValue(OffsetXProperty);
            }
            set
            {
                SetValue(OffsetXProperty, value);
            }
        }
        public static readonly DependencyProperty OffsetXProperty = DependencyProperty.Register("OffsetX", typeof(Int16), typeof(ImageCanvas), new FrameworkPropertyMetadata(default(Int16), PropertyChangedCallback));


        public Int16 OffsetY
        {
            get
            {
                return (Int16)GetValue(OffsetYProperty);
            }
            set
            {
                SetValue(OffsetYProperty, value);
            }
        }
        public static readonly DependencyProperty OffsetYProperty = DependencyProperty.Register("OffsetY", typeof(Int16), typeof(ImageCanvas), new FrameworkPropertyMetadata(default(Int16), PropertyChangedCallback));



        private Pen pen1 = new Pen() { Brush = Brushes.White, DashStyle = new DashStyle(new double[] { 1, 3 }, 2), Thickness = 1, };
        private Pen pen2 = new Pen() { Brush = Brushes.Yellow, DashStyle = new DashStyle(new double[] { 1, 3 }, 2), Thickness = 1, };
        private Pen pen3 = new Pen() { Brush = Brushes.Blue, DashStyle = new DashStyle(new double[] { 1, 3 }, 2), Thickness = 1, };
        private Pen pen4 = new Pen() { Brush = Brushes.OrangeRed, DashStyle = new DashStyle(new double[] { 1, 3 }, 2), Thickness = 1 };


        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);



            if (Source != null)
            {
                var halfWidth = Math.Round(this.RenderSize.Width / 2);
                var halfHeight = Math.Round(this.RenderSize.Height / 2);


                var left = halfWidth + this.OffsetX;
                var top = halfHeight + this.OffsetY;
                dc.DrawImage(Source, new Rect(left - 0.5, top - 0.5, Source.Width, Source.Height));


                if (this.ZeroLine)
                {
                    dc.DrawLine(pen1, new Point(0, halfHeight), new Point(halfWidth * 2, halfHeight));
                    dc.DrawLine(pen1, new Point(halfWidth, 0), new Point(halfWidth, halfHeight * 2));
                }

                if (this.HumLine)
                {
                    dc.DrawLine(pen2, new Point(0, halfHeight + 20), new Point(halfWidth * 2, halfHeight + 20));
                    dc.DrawLine(pen2, new Point(halfWidth + 24, 0), new Point(halfWidth + 24, halfHeight * 2));
                }

                if (this.BoundsLine)
                {
                    dc.DrawRectangle(Brushes.Transparent, pen3, new Rect(left - 1, top - 1, Source.Width + 1, Source.Height + 1));
                }

                if (!this.CustomRect.IsEmpty)
                {
                    var dst = new Rect(halfWidth, halfHeight, this.CustomRect.Width, this.CustomRect.Height);
                    dc.DrawRectangle(Brushes.Transparent, pen4, dst);
                }
            }

            this.RenderSize = new Size(this.Width, this.Height);
        }


        private Point? mousePoint { get; set; }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            this.mousePoint = e.GetPosition(this).Round(0);
            base.OnMouseLeftButtonDown(e);
        }


        protected override void OnMouseMove(MouseEventArgs e)
        {
            var point = e.GetPosition(this).Round(0);
            if (e.LeftButton == MouseButtonState.Pressed && mousePoint != null)
            {
                var p = point - this.mousePoint.Value;
                this.OffsetX += (short)p.X;
                this.OffsetY += (short)p.Y;
                this.mousePoint = point;

                this.InvalidateVisual();
            }
            base.OnMouseMove(e);
        }


        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            this.mousePoint = null;
            base.OnMouseLeftButtonUp(e);
        }


        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.Key)
            {

                case Key.Up:
                    this.OffsetY--;
                    break;
                case Key.Down:
                    this.OffsetY++;
                    break;

                case Key.Left:
                    this.OffsetX--;
                    break;

                case Key.Right:
                    this.OffsetX++;
                    break;

            }

        }


    }
}
