using System;
using System.Windows;

namespace Xaml.Effects.Toolkit.Styles.Windows
{
    public class ServiceCollection : FreezableCollection<IService>
    {
        public ServiceCollection()
        {

        }

        internal void Initialize(Window _window)
        {
            window = _window;
            foreach (var service in this)
            {
                try
                {
                    service.Attach(window);
                }
                catch (Exception)
                {
                }
            }
        }
        internal void UnInitialize()
        {
            foreach (var service in this)
            {
                try
                {
                    service.UnAttach();
                }
                catch (Exception)
                {
                }
            }
        }
        internal IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            foreach (var service in this)
            {
                try
                {
                    var result = service.RouteMessage(hwnd, msg, wParam, lParam, ref handled);
                    if (handled)
                    {
                        return result;
                    }
                }
                catch (Exception)
                {
                }
            }
            return IntPtr.Zero;
        }
        private Window window { get; set; }
    }
}
