using Assets.Editor.Utils;
using System.Windows;

namespace Assets.Editor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            ConfigureUtil.Init();
        }

    }
}
