using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xaml.Effects.Toolkit.Common;

namespace Xaml.Effects.Toolkit.Controls
{
    public class ToolboxItem : ContentControl
    {
        // caches the start point of the drag operation
        private Point? dragStartPoint = null;

        static ToolboxItem()
        {
            // set the key to reference the style for this control
            FrameworkElement.DefaultStyleKeyProperty.OverrideMetadata(typeof(ToolboxItem), new FrameworkPropertyMetadata(typeof(ToolboxItem)));
        }

















        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            this.dragStartPoint = new Point?(e.GetPosition(this));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.LeftButton != MouseButtonState.Pressed)
                this.dragStartPoint = null;
            if (this.dragStartPoint.HasValue)
            {
                DragObject dataObject = new DragObject(this, DragTypes.Control, this.Content);
                this.GiveFeedback += DragSource_GiveFeedback;
                DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Copy);
                this.GiveFeedback -= DragSource_GiveFeedback;
                e.Handled = true;
            }
        }



        void DragSource_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.Handled = true;
        }





    }


}
