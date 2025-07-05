using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Xaml.Effects.Toolkit.Uitity;

namespace Xaml.Effects.Toolkit.Adorners
{
    public class SelectionEventArgs : EventArgs
    {
        internal SelectionEventArgs(Point? start, Point? end)
        {
            if (start.HasValue && end.HasValue)
            {
                Start = start.Value;
                End = end.Value;
                Range = new Rect(Start, End);
                IsEffective = true;
            }
        }

        /// <summary>
        /// 有效的选区
        /// </summary>
        public Boolean IsEffective { get; private set; }

        /// <summary>
        /// 选区范围
        /// </summary>
        public Rect Range { get; private set; }

        /// <summary>
        /// 选区开始位置
        /// </summary>
        public Point Start { get; private set; }

        /// <summary>
        /// 选区结束位置
        /// </summary>
        public Point End { get; private set; }
    }



    public delegate void RubberbandSelectionEventHandler(RubberbandAdorner sender, SelectionEventArgs e);
    public delegate void RubberbandAdornerEventHandler(RubberbandAdorner sender, SelectionEventArgs e);


    /// <summary>
    /// Canvas 橡皮圈 选区装饰器
    /// </summary>
    public class RubberbandAdorner : Adorner
    {
        /// <summary>
        /// 装饰器被附加到对象上触发事件
        /// </summary>
        public event RubberbandAdornerEventHandler OnAttach;
        /// <summary>
        /// 装饰器从象上分离触发事件
        /// </summary>
        public event RubberbandAdornerEventHandler OnDetach;

        /// <summary>
        /// 装饰器选区变化事件
        /// </summary>
        public event RubberbandSelectionEventHandler OnUpdateSelection;

        /// <summary>
        /// 创建一个橡皮圈装饰器
        /// </summary>
        /// <param name="host">宿主对象</param>
        /// <param name="dragStartPoint">橡皮圈开始位置</param>
        public RubberbandAdorner(UIElement host)
            : base(host)
        {
            this.PointDecimals = 0;
            this.host = host;
            rubberbandPen = new Pen(Brushes.LightSlateGray, 1);
            rubberbandPen.DashStyle = new DashStyle(new double[] { 2 }, 1);
        }

        /// <summary>
        /// 附加显示
        /// </summary>
        public void Attach(Point dragStartPoint)
        {
            this.startPoint = dragStartPoint.Round(this.PointDecimals);
            AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(this.host);
            if (adornerLayer != null)
            {
                adornerLayer.Add(this);
                OnAttach?.Invoke(this, this.getEventArgs());
                if (!this.IsMouseCaptured)
                    this.CaptureMouse();
            }
        }


        /// <summary>
        /// 分离  左键弹开自动分离
        /// </summary>
        public void Detach()
        {
            AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(this.host);
            if (adornerLayer != null)
            {
                adornerLayer.Remove(this);
                OnDetach?.Invoke(this, this.getEventArgs());
            }
        }

        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                endPoint = e.GetPosition(this).Round(this.PointDecimals);
                OnUpdateSelection?.Invoke(this, this.getEventArgs());
                this.InvalidateVisual();
            }
            else
            {
                if (this.IsMouseCaptured) this.ReleaseMouseCapture();
            }

            e.Handled = true;
        }



        private SelectionEventArgs getEventArgs()
        {
            if (startPoint.HasValue && endPoint.HasValue)
            {
                var minX = Math.Min(startPoint.Value.X, endPoint.Value.X);
                var minY = Math.Min(startPoint.Value.Y, endPoint.Value.Y);
                var maxX = Math.Max(startPoint.Value.X, endPoint.Value.X);
                var maxY = Math.Max(startPoint.Value.Y, endPoint.Value.Y);
                return new SelectionEventArgs(new Point(minX, minY), new Point(maxX, maxY));
            }
            return new SelectionEventArgs(null, null);
        }

        protected override void OnMouseUp(System.Windows.Input.MouseButtonEventArgs e)
        {
            // release mouse capture
            if (this.IsMouseCaptured) this.ReleaseMouseCapture();
            // remove this adorner from adorner layer
            this.Detach();
            e.Handled = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            // without a background the OnMouseMove event would not be fired!
            // Alternative: implement a Canvas as a child of this adorner, like
            // the ConnectionAdorner does.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

            if (this.startPoint.HasValue && this.endPoint.HasValue)
                dc.DrawRectangle(Brushes.Transparent, rubberbandPen, new Rect(this.startPoint.Value, this.endPoint.Value));
        }


        /// <summary>
        /// 坐标小数位
        /// </summary>
        public Int32 PointDecimals
        {
            get;
            set;
        }

        #region Private Property
        private Point? startPoint;
        private Point? endPoint;
        private Pen rubberbandPen;
        private UIElement host;
        #endregion




        //private void UpdateSelection()
        //{
        //    designerCanvas.SelectionService.ClearSelection(false);

        //    
        //    foreach (Control item in designerCanvas.Children)
        //    {
        //        if (item is IControlRectangle control)
        //        {
        //            var itemBounds = control.Rectangle;
        //            if (rubberBand.Contains(itemBounds))
        //            {
        //                if (item is Connection)
        //                    designerCanvas.SelectionService.AddToSelection(item as ISelectable);
        //                else
        //                {
        //                    DesignerItem di = item as DesignerItem;
        //                    if (di.Infomation.ParentID == Guid.Empty)
        //                        designerCanvas.SelectionService.AddToSelection(di);
        //                }
        //            }
        //        }




        //        //Rect itemRect = VisualTreeHelper.GetDescendantBounds(item);
        //        //Rect itemBounds = item.TransformToAncestor(designerCanvas).TransformBounds(itemRect);

        //        //if (rubberBand.Contains(itemBounds))
        //        //{
        //        //    if (item is Connection)
        //        //        designerCanvas.SelectionService.AddToSelection(item as ISelectable);
        //        //    else
        //        //    {
        //        //        DesignerItem di = item as DesignerItem;
        //        //        if (di.Infomation.ParentID == Guid.Empty)
        //        //            designerCanvas.SelectionService.AddToSelection(di);
        //        //    }
        //        //}
        //    }
        //    designerCanvas.SelectionService.Update();
        //}
    }
}
