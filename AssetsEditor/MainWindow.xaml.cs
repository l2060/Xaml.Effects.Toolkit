using Assets.Editor.Models;
using System;
using System.Linq;
using System.Windows;

namespace Assets.Editor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }



        public MainWindowModel Model { get; set; } = new MainWindowModel();

        public MainWindow()
        {
            this.DataContext = Model;
            InitializeComponent();
            this.Model.OnClose += Model_OnClose;
            Instance = this;
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            var filename = Environment.GetCommandLineArgs().Where(e => e.ToLower().EndsWith(".asset")).FirstOrDefault();
            if (filename != null)
            {
                this.Model.OpenFile(filename);
            }

        }



        private void Model_OnClose(object sender, Xaml.Effects.Toolkit.Model.WindowDestroyArgs e)
        {
            Environment.Exit(0);
        }
    }
}
