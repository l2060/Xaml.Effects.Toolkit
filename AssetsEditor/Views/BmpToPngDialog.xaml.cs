using Assets.Editor.Models;
using System.Windows;

namespace Assets.Editor.Views
{
    /// <summary>
    /// PasswordInput.xaml 的交互逻辑
    /// </summary>
    public partial class BmpToPngDialog : Window
    {

        public BmpToPngModel Model { get; set; } = new BmpToPngModel();

        public BmpToPngDialog()
        {
            this.DataContext = Model;
            InitializeComponent();
            this.Model.OnClose += Model_OnClose;
        }

        private void Model_OnClose(object sender, Xaml.Effects.Toolkit.Model.WindowDestroyArgs e)
        {
            this.DialogResult = e.DialogResult;
        }

    }
}
