using Assets.Editor.Models;
using System.Windows;

namespace Assets.Editor.Views
{
    /// <summary>
    /// PasswordInput.xaml 的交互逻辑
    /// </summary>
    public partial class ExportSpriteFramesDialog : Window
    {

        public ExportSpriteFrameModel Model { get; set; } = new ExportSpriteFrameModel();

        public ExportSpriteFramesDialog()
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
