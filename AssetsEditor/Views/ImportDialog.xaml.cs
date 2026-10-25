using Assets.Editor.Models;
using System.Windows;
using System.ComponentModel;

namespace Assets.Editor.Views
{
    /// <summary>
    /// PasswordInput.xaml 的交互逻辑
    /// </summary>
    public partial class ImportDialog : Window
    {

        public ImportDialogModel Model { get; set; } = new ImportDialogModel();

        public ImportDialog()
        {
            this.DataContext = Model;
            InitializeComponent();
            this.Model.OnClose += Model_OnClose;
        }

        private void Model_OnClose(object sender, Xaml.Effects.Toolkit.Model.WindowDestroyArgs e)
        {
            e.Apply(this);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!Model.RequestWindowClose()) e.Cancel = true;
            base.OnClosing(e);
        }

    }
}
