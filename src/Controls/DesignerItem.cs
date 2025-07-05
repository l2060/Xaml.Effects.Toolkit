using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xaml.Effects.Toolkit.Adorners;
using Xaml.Effects.Toolkit.Uitity;

namespace Xaml.Effects.Toolkit.Controls
{
    //[ContentProperty("Children")]
    public class DesignerItem : ItemsControl
    {
        static DesignerItem()
        {
            FrameworkElement.DefaultStyleKeyProperty.OverrideMetadata(typeof(DesignerItem), new FrameworkPropertyMetadata(typeof(DesignerItem)));
        }

        public DesignerItem(/*IProxy proxy*/)
        {
            Canvas.SetLeft(this, 0);
            Canvas.SetTop(this, 0);
            //this.BindProxy(proxy);
            this.Loaded += new RoutedEventHandler(DesignerItem_Loaded);
            this.Unloaded += new RoutedEventHandler(DesignerItem_Unloaded);
            this.LayoutUpdated += DesignerItem_LayoutUpdated;
        }

        #region  Layout Events
        private void DesignerItem_Loaded(object sender, RoutedEventArgs e)
        {
            this.designerCanvas = this.FindParent<DesignerCanvas>();

            //if (parentCanvas != null)
            //{
            //    canvas = parentCanvas;
            //    if (this.Proxy != null)
            //    {
            //        Proxy.Loaded(parentCanvas);
            //    }
            //}
        }

        private void DesignerItem_Unloaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= new RoutedEventHandler(DesignerItem_Loaded);
            this.Unloaded -= new RoutedEventHandler(DesignerItem_Unloaded);
            this.LayoutUpdated -= DesignerItem_LayoutUpdated;

        }

        private void DesignerItem_LayoutUpdated(object sender, EventArgs e)
        {
            if (this.Parent == null)
            {
                return;
            }

            var rect = new Rect(new Point(Canvas.GetLeft(this), Canvas.GetTop(this)), new Size(this.Width, this.Height));



            //if (this.Infomation != null)
            //{
            //    this.Infomation.ZIndex = Canvas.GetZIndex(this);
            //}
            //Rect itemRect = VisualTreeHelper.GetDescendantBounds(this);
            //this.Rectangle = this.TransformToAncestor(this.Parent).TransformBounds(itemRect);
            //var Position = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));// this.TransformToAncestor(canvas).Transform(new Point());
            //var center = new Point(Position.X + this.Width / 2, Position.Y + this.Height / 2);
            //if (center.Equals(this.Center))
            //{
            //    return;
            //}
            //var rect = new Rect(Position, new Size(this.Width, this.Height));
            //if (Proxy != null)
            //{
            //    Proxy.UpdatePosition(rect);
            //}
            //this.Center = center;
            //PropertyChange?.Invoke(PropertyType.Position);
        }
        #endregion

        #region Mouse Events


        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (this.designerCanvas != null)
            {
                this.designerCanvas.SelectedItem = this;
                //
                this.IsFocus = true;
                this.Focus();
            }
        }



        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Console.WriteLine(this.Width);
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
        }
        #endregion

        #region override default property
        public new readonly static DependencyProperty VerticalAlignmentProperty = DependencyProperty.Register("VerticalAlignment", typeof(VerticalAlignment), typeof(DesignerItem), new PropertyMetadata(VerticalAlignment.Top));
        public new readonly static DependencyProperty HorizontalAlignmentProperty = DependencyProperty.Register("HorizontalAlignment", typeof(HorizontalAlignment), typeof(DesignerItem), new PropertyMetadata(HorizontalAlignment.Left));
        #endregion

        #region IsCanResize
        public Boolean IsCanResize
        {
            get { return (Boolean)GetValue(IsCanResizeProperty); }
            set { SetValue(IsCanResizeProperty, value); }
        }
        public static readonly DependencyProperty IsCanResizeProperty = DependencyProperty.Register("IsCanResize",
                                                                                            typeof(Boolean),
                                                                                            typeof(DesignerItem),
                                                                                            new PropertyMetadata(true));
        #endregion

        #region IsSelected Property
        //public bool IsSelected
        //{
        //    get
        //    {
        //        return (bool)GetValue(IsSelectedProperty);
        //    }
        //    set
        //    {
        //        SetValue(IsSelectedProperty, value);
        //        if (value)
        //        {
        //            IsFocus = true;
        //        }
        //    }
        //}
        //public static readonly DependencyProperty IsSelectedProperty =
        //  DependencyProperty.Register("IsSelected",
        //                               typeof(bool),
        //                               typeof(DesignerItem),
        //                               new FrameworkPropertyMetadata(false));
        #endregion

        #region IsFocus

        public bool IsFocus
        {
            get
            {
                return (bool)GetValue(IsFocusProperty);
            }
            set
            {
                SetValue(IsFocusProperty, value);
            }
        }

        public static readonly DependencyProperty IsFocusProperty =
          DependencyProperty.Register("IsFocus",
                                       typeof(bool),
                                       typeof(DesignerItem),
                                       new FrameworkPropertyMetadata(false));

        #endregion

        #region Left
        public Double Left
        {
            get
            {
                return (Double)GetValue(LeftProperty);
            }
            set
            {
                SetValue(LeftProperty, value);
            }
        }
        public static readonly DependencyProperty LeftProperty =
            DependencyProperty.Register("Left", typeof(Double), typeof(DesignerItem), new PropertyMetadata(0D, OnLeftChanged));

        private static void OnLeftChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var sender = o as DesignerItem;
            Canvas.SetLeft(sender, (Double)e.NewValue);
            sender.InvalidateMeasure();
        }
        #endregion

        #region Top
        public Double Top
        {
            get
            {
                return (Double)GetValue(TopProperty);
            }
            set
            {
                SetValue(TopProperty, value);
            }
        }
        public static readonly DependencyProperty TopProperty =
            DependencyProperty.Register("Top", typeof(Double), typeof(DesignerItem), new PropertyMetadata(0D, OnTopChanged));

        private static void OnTopChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var sender = o as DesignerItem;
            Canvas.SetTop(sender, (Double)e.NewValue);
            sender.InvalidateMeasure();
        }
        #endregion

        #region BackgroundStyle
        /// <summary>
        /// 图片源
        /// </summary>
        public BackgroundStyle BackgroundStyle
        {
            get { return (BackgroundStyle)base.GetValue(BackgroundStyleProperty); }
            set { base.SetValue(BackgroundStyleProperty, value); }
        }

        /// <summary>
        /// 图片源
        /// </summary>
        public static readonly DependencyProperty BackgroundStyleProperty = DependencyProperty.Register("BackgroundStyle",
        typeof(BackgroundStyle), typeof(DesignerItem),
        new FrameworkPropertyMetadata(BackgroundStyle.SolidColor, FrameworkPropertyMetadataOptions.AffectsRender));
        #endregion

        #region Thickness
        /// <summary>
        /// 图片四个边距
        /// </summary>
        public Thickness Thickness
        {
            get { return (Thickness)GetValue(ThicknessProperty); }
            set { SetValue(ThicknessProperty, value); }
        }
        /// <summary>
        /// 图片四个边距
        /// </summary>
        public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register("Thickness", typeof(Thickness), typeof(DesignerItem),
        new FrameworkPropertyMetadata(new Thickness(), FrameworkPropertyMetadataOptions.AffectsRender));

        #endregion

        #region OnDrop


        protected override void OnDragEnter(DragEventArgs e)
        {
            DragPreviewAdorner.Attach(this, e.Data);
            e.Handled = true;
        }
        #endregion

        #region RotateAngle
        public Double RotateAngle
        {
            get
            {

                return (Double)GetValue(RotateAngleProperty);
            }
            set
            {
                SetValue(RotateAngleProperty, value);
                // PropertyChange?.Invoke(PropertyType.Angle);
                //if (this.Proxy != null)
                //{
                //    this.Proxy.UpdateRotateAngle(value);
                //}
            }
        }
        public static readonly DependencyProperty RotateAngleProperty =
            DependencyProperty.Register("RotateAngle", typeof(Double), typeof(DesignerItem), new PropertyMetadata(0D, OnRotateAngleChanged));

        private static void OnRotateAngleChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var sender = o as DesignerItem;
            var Transform = sender.RenderTransform as RotateTransform;
            if (Transform == null)
            {
                Transform = new RotateTransform((Double)e.NewValue);
                sender.RenderTransform = Transform;
            }
            Transform.Angle = (Double)e.NewValue;
            sender.InvalidateMeasure();
        }




        #endregion

        #region Property Proxy

        //public IPropertyProxy Proxy { get; set; }

        #endregion


        #region Private Property

        private DesignerCanvas designerCanvas { get; set; }
        #endregion
    }
}
