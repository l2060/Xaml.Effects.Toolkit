using System.Windows.Controls;
using System.Windows.Input;

namespace Xaml.Effects.Toolkit.Controls
{
    public class DragScrollViewer : ScrollViewer
    {
        public DragScrollViewer()
        {
            this.Loaded += DragScrollViewer_Loaded;
            this.Unloaded += DragScrollViewer_Unloaded;
        }

        private void DragScrollViewer_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            this.Loaded -= DragScrollViewer_Loaded;
            this.Unloaded -= DragScrollViewer_Unloaded;
            this.RequestBringIntoView -= DragScrollViewer_RequestBringIntoView;
        }

        private void DragScrollViewer_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            this.RequestBringIntoView += DragScrollViewer_RequestBringIntoView;
        }

        private void DragScrollViewer_RequestBringIntoView(object sender, System.Windows.RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                ScrollMousePoint1 = e.GetPosition(this);
                HorizontalOff1 = this.HorizontalOffset;
                VerticalOff1 = this.VerticalOffset;
                this.CaptureMouse();
            }
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            if (this.IsMouseCaptured)
            {
                this.ScrollToHorizontalOffset(HorizontalOff1 + (ScrollMousePoint1.X - e.GetPosition(this).X));
                this.ScrollToVerticalOffset(VerticalOff1 + (ScrollMousePoint1.Y - e.GetPosition(this).Y));
            }
        }

        protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
            this.ReleaseMouseCapture();
        }

        System.Windows.Point ScrollMousePoint1 = new System.Windows.Point();
        double HorizontalOff1 = 1;
        double VerticalOff1 = 1;
    }
}
