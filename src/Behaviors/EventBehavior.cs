using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Input;

namespace Xaml.Effects.Toolkit.Behaviors
{
    public class EventBehavior : TriggerAction<FrameworkElement>
    {
        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register("Command", typeof(ICommand), typeof(EventBehavior), new PropertyMetadata(null));

        /// <summary>
        /// 要执行的 Action 
        /// </summary>
        public ICommand Command
        {
            get { return (ICommand)GetValue(CommandProperty); }
            set { SetValue(CommandProperty, value); }
        }


        public static readonly DependencyProperty HandlerProperty = DependencyProperty.Register("Handler", typeof(IEventCommand), typeof(EventBehavior), new PropertyMetadata(null));

        /// <summary>
        /// 要执行的 Action 
        /// </summary>
        public IEventCommand Handler
        {
            get { return (IEventCommand)GetValue(HandlerProperty); }
            set { SetValue(HandlerProperty, value); }
        }



        public static readonly DependencyProperty ActionProperty = DependencyProperty.Register("Action", typeof(Action<FrameworkElement, Object>), typeof(EventBehavior), new PropertyMetadata(null));

        /// <summary>
        /// 要执行的 Action 
        /// </summary>
        public Action<FrameworkElement, Object> Action
        {
            get { return (Action<FrameworkElement, Object>)GetValue(ActionProperty); }
            set { SetValue(ActionProperty, value); }
        }



        protected override void Invoke(object parameter)
        {
            if (Handler != null)
            {
                Handler.Execute(this.AssociatedObject, parameter);
            }

            if (Action != null)
            {
                Action(this.AssociatedObject, parameter);
            }

            if (Command != null)
            {
                Command.Execute(parameter);
            }
        }
    }
}
