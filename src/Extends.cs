using System.Reflection;
using System.Windows;

namespace Xaml.Effects.Toolkit
{
    public static class Extends
    {
        public static bool IsModal(this Window window)
        {
            var filedInfo = typeof(Window).GetField("_showingAsDialog", BindingFlags.Instance | BindingFlags.NonPublic);

            return filedInfo != null && (bool)filedInfo.GetValue(window);
        }
    }
}
