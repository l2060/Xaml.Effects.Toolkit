using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace Xaml.Effects.Toolkit.Controls
{
    /// <summary>
    /// 弹出控件，当控件失去焦点时自动关闭。
    /// </summary>
    [ContentProperty("Content")]
    public class PopupControl : Control
    {
        /// <summary>
        /// PopupContent，弹出内容。
        /// </summary>
        public FrameworkElement Content
        {
            get { return (FrameworkElement)GetValue(ContentProperty); }
            set { SetValue(ContentProperty, value); }
        }
        /// <summary>
        /// PopupContent，弹出内容。
        /// </summary>
        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register("Content",
            typeof(FrameworkElement), typeof(PopupControl), new UIPropertyMetadata(null, OnContentChanged));

        public static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as PopupControl;

            control.UpdateContent();
        }

        /// <summary>
        /// IsOpen，是否显示弹出内容。
        /// </summary>
        public bool IsOpen
        {
            get
            {
                return (bool)GetValue(IsOpenProperty);
            }
            set
            {
                SetValue(IsOpenProperty, value);
            }
        }
        /// <summary>
        /// IsOpen，是否显示弹出内容，依赖属性。
        /// </summary>
        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register("IsOpen", typeof(bool), typeof(PopupControl),
            new UIPropertyMetadata(false, IsOpen_Changed));
        private static void IsOpen_Changed(DependencyObject s, DependencyPropertyChangedEventArgs e)
        {
            var Owner = s as PopupControl;
            Owner.SetAdonersVisibility();
        }

        // Pri
        private AdornerLayer ThisAdornerLayer = null;

        private PopupControlContentAdorner ContentAdorner = null;

        /// <summary>
        /// T4PopupControl，构造函数。
        /// </summary>
        public PopupControl()
        {
            this.DefaultStyleKey = typeof(PopupControl);
            this.Loaded -= T4PopupControl_Loaded;
            this.Loaded += T4PopupControl_Loaded;
            this.Unloaded += T4PopupControl_Unloaded;
        }

        private void T4PopupControl_Unloaded(object sender, RoutedEventArgs e)
        {
            ThisAdornerLayer.Remove(ContentAdorner);
        }

        private void T4PopupControl_Loaded(object sender, RoutedEventArgs e)
        {
            ThisAdornerLayer = AdornerLayer.GetAdornerLayer(this);
            ContentAdorner = new PopupControlContentAdorner(this, this.Content);
            ThisAdornerLayer.Add(ContentAdorner);
            SetAdonersVisibility();
        }

        private void UpdateContent()
        {
            if (ContentAdorner != null)
            {
                ContentAdorner.Content = this.Content;
            }
        }


        private void SetAdonersVisibility()
        {
            if (ThisAdornerLayer != null)
            {
                if (this.IsOpen)
                {
                    ContentAdorner.Show();
                }
                else
                {
                    ContentAdorner.Hide();
                }
            }
        }

    }

    internal class PopupControlContentAdorner : Adorner
    {
        private FrameworkElement _content;

        public FrameworkElement Content
        {
            get
            {
                return _content;
            }
            set
            {
                if (_content != null)
                {
                    this.RemoveVisualChild(_content);
                }
                _content = value;
                if (_content != null)
                {
                    this.AddVisualChild(_content);
                }
            }
        }
        internal PopupControlContentAdorner(UIElement AdornedElement, FrameworkElement ContentVisual)
            : base(AdornedElement)
        {
            this.Content = ContentVisual;
            this.IsHitTestVisible = true;
        }
        protected override int VisualChildrenCount { get { return 1; } }
        protected override Visual GetVisualChild(int index) { return this.Content; }
        protected override Size MeasureOverride(Size constraint)
        {
            if (Content != null)
            {
                Content.Measure(constraint);
            }

            return constraint;
        }

        protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsKeyboardFocusWithinChanged(e);
            var parent = AdornedElement as PopupControl;
            if (!this.IsKeyboardFocusWithin)
            {

                parent.IsOpen = false;
            }
            else
            {
                parent.IsOpen = true;
            }
        }


        protected override Size ArrangeOverride(Size finalSize)
        {
            if (this.Content != null)
            {
                this.Content.Arrange(new Rect()
                {
                    X = 0,
                    Y = 0,
                    Width = finalSize.Width,
                    Height = finalSize.Height
                });
            }

            return base.ArrangeOverride(finalSize);
        }

        public void Show()
        {
            this.Visibility = Visibility.Visible;
            if (this.Content != null)
            {
                this.Content.Focus();
            }

        }

        public void Hide()
        {
            this.Visibility = Visibility.Collapsed;
        }
    }
}
