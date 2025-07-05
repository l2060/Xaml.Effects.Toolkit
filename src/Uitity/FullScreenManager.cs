using System;
using System.Windows;
using System.Windows.Interop;

namespace Xaml.Effects.Toolkit.Uitity
{
    public static class FullScreenManager
    {
        public static void RepairWpfWindowFullScreenBehavior(Window wpfWindow)
        {
            if (wpfWindow == null)
            {
                return;
            }

            if (wpfWindow.WindowState == WindowState.Maximized)
            {
                wpfWindow.WindowState = WindowState.Normal;
                wpfWindow.Loaded += delegate { wpfWindow.WindowState = WindowState.Maximized; };
            }

            wpfWindow.SourceInitialized += delegate
            {
                IntPtr handle = (new WindowInteropHelper(wpfWindow)).Handle;
                HwndSource source = HwndSource.FromHwnd(handle);
                if (source != null)
                {
                    source.AddHook(WindowProc);
                }
            };
        }

        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x0024:
                    Win32Api.GetMinMaxInfo(hwnd, lParam);
                    handled = true;
                    break;
            }

            return (IntPtr)0;
        }
    }
}
