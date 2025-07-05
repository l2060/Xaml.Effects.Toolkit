using System;
using System.Windows.Threading;

namespace Xaml.Effects.Toolkit.Uitity
{
    public class UIThread
    {



        public static void Invoke(Action callback)
        {
            Dispatcher.CurrentDispatcher.Invoke(callback);
        }

        public static void Invoke(Action callback, DispatcherPriority priority)
        {
            Dispatcher.CurrentDispatcher.Invoke(callback, priority);
        }

        public static void Invoke(Delegate method, params object[] args)
        {
            Dispatcher.CurrentDispatcher.Invoke(method, args);
        }


    }
}
