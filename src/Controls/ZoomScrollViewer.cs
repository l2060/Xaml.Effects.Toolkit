using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Xaml.Effects.Toolkit.Controls
{
    public class ZoomScrollViewer : DragScrollViewer
    {
        public ZoomScrollViewer()
        {
            CanContentScroll = false;
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Visible;
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Visible;
            this.scaleTransform = new ScaleTransform();
            this.PreviewMouseWheel += new MouseWheelEventHandler(this.DesignerCanvas_MouseWheel);
            this.LayoutUpdated += ZoomScrollViewer_LayoutUpdated;
        }

        private void ZoomScrollViewer_LayoutUpdated(object sender, EventArgs e)
        {
            if (_content != this.Content)
            {
                _content = this.Content;
                if (_content != null)
                {
                    if (_content is FrameworkElement ui)
                    {
                        ui.LayoutTransform = this.scaleTransform;
                    }
                }
            }
        }


        private void DesignerCanvas_MouseWheel(object sender, EventArgs e)
        {
            MouseWheelEventArgs wheel = (MouseWheelEventArgs)e;
            double value = Math.Min(wheel.Delta / 1200d, 10);
            var zoom = Math.Round(this.ZoomValue + value, 2);



            if (zoom > MaxZoomValue) zoom = MaxZoomValue;
            if (zoom < MinZoomValue) zoom = MinZoomValue;




            this.ZoomValue = zoom;
        }

        #region ZoomValue

        public Double ZoomValue
        {
            get
            {
                return (Double)GetValue(ZoomValueProperty);
            }
            set
            {
                if (this.ZoomValue != value)
                {
                    UpdateZoomLayout(this.ZoomValue, value);
                    SetValue(ZoomValueProperty, value);
                }
            }
        }

        public static readonly DependencyProperty ZoomValueProperty =
          DependencyProperty.Register("ZoomValue",
                                       typeof(Double),
                                       typeof(ZoomScrollViewer),
                                       new FrameworkPropertyMetadata(1d, ZoomValuePropertyChanged), ZoomValueValidateValue);
        private static void ZoomValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = d as ZoomScrollViewer;
            if (view != null)
            {
                view.UpdateZoomLayout((Double)e.OldValue, (Double)e.NewValue);
            }
        }


        //验证
        private static bool ZoomValueValidateValue(object obj)
        {
            if (obj is Double d)
            {
                if (d > 0 && d <= 100)
                {
                    return true;
                }
            }
            return false;
        }



        private void UpdateZoomLayout(Double oldValue, Double value)
        {
            if (Double.IsNaN(lastZoomValue) || lastZoomValue != value)
            {
                value = value < 0.1 ? 0.1 : value;
                double scale = Math.Round(value / oldValue, 2);
                double halfViewportHeight = this.ViewportHeight / 2;
                double newVerticalOffset = ((this.VerticalOffset + halfViewportHeight) * scale - halfViewportHeight);
                double halfViewportWidth = this.ViewportWidth / 2;
                double newHorizontalOffset = ((this.HorizontalOffset + halfViewportWidth) * scale - halfViewportWidth);
                this.scaleTransform.ScaleX *= scale;
                this.scaleTransform.ScaleY *= scale;
                this.ScrollToHorizontalOffset(newHorizontalOffset);
                this.ScrollToVerticalOffset(newVerticalOffset);
                lastZoomValue = value;

            }



        }

        private Double lastZoomValue = Double.NaN;


        #endregion






        #region MaxZoomValue

        public Double MaxZoomValue
        {
            get
            {
                return (Double)GetValue(MaxZoomValueProperty);
            }
            set
            {
                SetValue(MaxZoomValueProperty, value);
            }
        }

        public static readonly DependencyProperty MaxZoomValueProperty =
          DependencyProperty.Register("MaxZoomValue",
                                       typeof(Double),
                                       typeof(ZoomScrollViewer),
                                       new FrameworkPropertyMetadata(100d));

        #endregion





        #region MaxZoomValue

        public Double MinZoomValue
        {
            get
            {
                return (Double)GetValue(MinZoomValueProperty);
            }
            set
            {
                SetValue(MinZoomValueProperty, value);
            }
        }

        public static readonly DependencyProperty MinZoomValueProperty =
          DependencyProperty.Register("MinZoomValue",
                                       typeof(Double),
                                       typeof(ZoomScrollViewer),
                                       new FrameworkPropertyMetadata(0.1d));

        #endregion






        private Object _content;

        private ScaleTransform scaleTransform;
    }
}
