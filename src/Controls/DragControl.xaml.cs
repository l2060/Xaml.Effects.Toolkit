using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Xaml.Effects.Toolkit.Controls
{
    /// <summary>
    /// DragControl.xaml 的交互逻辑
    /// </summary>
    public partial class DragControl : ContentControl
    {
        public DragControl()
        {
            InitializeComponent();

            AddHandler(Thumb.DragStartedEvent, new RoutedEventHandler(Drag_Started));
            AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(Drag_Delta));
            AddHandler(Thumb.DragCompletedEvent, new RoutedEventHandler(Drag_Completed));
            AddHandler(Thumb.GotFocusEvent, new RoutedEventHandler(OnGotFocus));
            AddHandler(Thumb.LostFocusEvent, new RoutedEventHandler(OnLostFocus));
            this.DataContext = this;
            ThumbOpacity = 1.0f;
        }
        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            ThumbVisible = Visibility.Collapsed;
        }
        private void OnGotFocus(object sender, RoutedEventArgs e)
        {
            ThumbVisible = Visibility.Visible;
        }

        private void Drag_Started(object sender, RoutedEventArgs e)
        {
            ThumbOpacity = 0.1;
            this.RaiseEvent(new RoutedEventArgs(OnDragStartedEvent, this));
        }


        private void Drag_Delta(object sender, DragDeltaEventArgs e)
        {
            Thumb thumb = e.OriginalSource as Thumb;
            if (thumb == null)
            {
                return;
            }
            Int32 DragDirection = -1;
            if (!Int32.TryParse((String)thumb.Tag, out DragDirection))
            {
                return;
            }
            double VerticalChange = e.VerticalChange;
            double HorizontalChange = e.HorizontalChange;
            Double left = Canvas.GetLeft(this);
            Double top = Canvas.GetTop(this);
            Double width = this.Width;
            Double height = this.Height;

            switch (DragDirection)
            {
                case 7:
                    height = this.Height - VerticalChange < 1 ? 1 : this.Height - VerticalChange;
                    top = Canvas.GetTop(this) + (this.Height - height);
                    width = this.Width - HorizontalChange < 1 ? 1 : this.Width - HorizontalChange;
                    left = Canvas.GetLeft(this) + (this.Width - width);
                    break;
                case 0:
                    height = this.Height - VerticalChange < 1 ? 1 : this.Height - VerticalChange;
                    top = Canvas.GetTop(this) + (this.Height - height);
                    break;
                case 1:
                    height = this.Height - VerticalChange < 1 ? 1 : this.Height - VerticalChange;
                    top = Canvas.GetTop(this) + (this.Height - height);
                    width = this.Width + HorizontalChange;
                    break;
                case 6:
                    width = this.Width - HorizontalChange < 1 ? 1 : this.Width - HorizontalChange;
                    left = Canvas.GetLeft(this) + (this.Width - width);
                    break;
                case 8:
                    left = Canvas.GetLeft(this) + HorizontalChange;
                    top = Canvas.GetTop(this) + VerticalChange;

                    //left = left < 0 ? 0 : left;
                    //top = top < 0 ? 0 : top;
                    break;
                case 2:
                    width = this.Width + HorizontalChange;
                    break;
                case 5:
                    width = this.Width - HorizontalChange < 1 ? 1 : this.Width - HorizontalChange;
                    left = Canvas.GetLeft(this) + (this.Width - width);
                    height = this.Height + VerticalChange;
                    break;
                case 4:
                    height = this.Height + VerticalChange;
                    break;
                case 3:
                    width = this.Width + HorizontalChange;
                    height = this.Height + VerticalChange;
                    break;
                default:
                    break;
            }
            //if (top < 0)
            //{
            //    height = height - Math.Abs(top);
            //    top = 0;
            //}




            width = width < 1 ? 1 : width;
            this.Width = width;
            height = height < 1 ? 1 : height;
            this.Height = height;
            Canvas.SetTop(this, top);
            Canvas.SetLeft(this, left);
            this.RaiseEvent(new RoutedEventArgs(OnDragDeltaEvent, this));
            //
            e.Handled = true;
        }
        private void Drag_Completed(object sender, RoutedEventArgs e)
        {
            Rect NewBound = new Rect
            {
                Y = Canvas.GetTop(this),
                X = Canvas.GetLeft(this),
                Width = this.ActualWidth,
                Height = this.ActualHeight
            };
            this.RaiseEvent(new RoutedEventArgs(OnDragCompletedEvent, this));
            ThumbOpacity = 1;
            e.Handled = true;
        }

        /// <summary>
        /// 声明路由事件
        /// 参数:要注册的路由事件名称，路由事件的路由策略，事件处理程序的委托类型(可自定义)，路由事件的所有者类类型
        /// </summary>
        public static readonly RoutedEvent OnDragStartedEvent = EventManager.RegisterRoutedEvent("OnDragStarted", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DragControl));
        /// <summary>
        /// 处理各种路由事件的方法 
        /// </summary>
        public event RoutedEventHandler OnDragStarted
        {
            //将路由事件添加路由事件处理程序
            add { AddHandler(OnDragStartedEvent, value, false); }
            //从路由事件处理程序中移除路由事件
            remove { RemoveHandler(OnDragStartedEvent, value); }
        }

        /// <summary>
        /// 声明路由事件
        /// 参数:要注册的路由事件名称，路由事件的路由策略，事件处理程序的委托类型(可自定义)，路由事件的所有者类类型
        /// </summary>
        public static readonly RoutedEvent OnDragCompletedEvent = EventManager.RegisterRoutedEvent("OnDragCompleted", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DragControl));
        /// <summary>
        /// 处理各种路由事件的方法 
        /// </summary>
        public event RoutedEventHandler OnDragCompleted
        {
            //将路由事件添加路由事件处理程序
            add { AddHandler(OnDragCompletedEvent, value, false); }
            //从路由事件处理程序中移除路由事件
            remove { RemoveHandler(OnDragCompletedEvent, value); }
        }

        /// <summary>
        /// 声明路由事件
        /// 参数:要注册的路由事件名称，路由事件的路由策略，事件处理程序的委托类型(可自定义)，路由事件的所有者类类型
        /// </summary>
        public static readonly RoutedEvent OnDragDeltaEvent = EventManager.RegisterRoutedEvent("OnDragDelta", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DragControl));
        /// <summary>
        /// 处理各种路由事件的方法 
        /// </summary>
        public event RoutedEventHandler OnDragDelta
        {
            //将路由事件添加路由事件处理程序
            add { AddHandler(OnDragDeltaEvent, value, false); }
            //从路由事件处理程序中移除路由事件
            remove { RemoveHandler(OnDragDeltaEvent, value); }
        }













        public readonly static DependencyProperty ThumbVisibleProperty =
            DependencyProperty.Register("ThumbVisible", typeof(Visibility), typeof(DragControl), new PropertyMetadata(Visibility.Visible));


        public Visibility ThumbVisible
        {
            get { return (Visibility)GetValue(ThumbVisibleProperty); }
            set { SetValue(ThumbVisibleProperty, value); }
        }


        public readonly static DependencyProperty ThumbOpacityProperty =
            DependencyProperty.Register("ThumbOpacity", typeof(Double), typeof(DragControl), new PropertyMetadata(1d));


        public Double ThumbOpacity
        {
            get { return (Double)GetValue(ThumbOpacityProperty); }
            set { SetValue(ThumbOpacityProperty, value); }
        }



        public readonly static DependencyProperty AnchorProperty =
            DependencyProperty.Register("Anchor", typeof(Thickness), typeof(DragControl), new PropertyMetadata(new Thickness(3)));


        public Thickness Anchor
        {
            get { return (Thickness)GetValue(AnchorProperty); }
            set { SetValue(AnchorProperty, value); }
        }


        public new readonly static DependencyProperty BorderBrushProperty =
            DependencyProperty.Register("BorderBrush", typeof(Brush), typeof(DragControl), new PropertyMetadata(new SolidColorBrush(Color.FromArgb(255, 0, 80, 245))));//#FF007ACC


        public new readonly static DependencyProperty BorderThicknessProperty =
            DependencyProperty.Register("BorderThickness", typeof(Thickness), typeof(DragControl), new PropertyMetadata(new Thickness(1)));



        public readonly static DependencyProperty LineBrushProperty =
            DependencyProperty.Register("LineBrush", typeof(Brush), typeof(DragControl), new PropertyMetadata(new SolidColorBrush(Color.FromArgb(255, 0, 80, 245))));//#FF007ACC


        public Brush LineBrush
        {
            get { return (Brush)GetValue(LineBrushProperty); }
            set { SetValue(LineBrushProperty, value); }
        }




    }
}
