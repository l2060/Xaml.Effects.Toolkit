using System;
using System.Windows;

namespace Xaml.Effects.Toolkit.Behaviors
{
    public interface IEventCommand
    {
        void Execute(FrameworkElement sender, object? parameter);
    }




    public class EventCommand<T> : IEventCommand
    {
        private Action<FrameworkElement, T> action;
        public EventCommand(Action<FrameworkElement, T> action)
        {
            this.action = action;
        }


        public void Execute(FrameworkElement sender, object parameter)
        {
            if (action != null) action(sender, (T?)parameter);
        }
    }




}
