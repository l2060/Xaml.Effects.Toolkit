using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Input;

namespace Xaml.Effects.Toolkit.Actions
{
    public class EventAction : TriggerAction<FrameworkElement>
    {
        public static readonly DependencyProperty ActionProperty = DependencyProperty.Register("Action", typeof(ICommand), typeof(EventAction), new PropertyMetadata(null));

        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register("CommandParameter", typeof(Object), typeof(EventAction), new PropertyMetadata(null));



        /// <summary>
        /// 要执行的 Action 
        /// </summary>
        public ICommand Action
        {
            get { return (ICommand)GetValue(ActionProperty); }
            set { SetValue(ActionProperty, value); }
        }



        public Object CommandParameter
        {
            get { return (Object)GetValue(CommandParameterProperty); }
            set { SetValue(CommandParameterProperty, value); }
        }







        protected override void Invoke(object parameter)
        {
            if (Action != null)
            {
                Action.Execute(CommandParameter);
            }
        }
    }
}
