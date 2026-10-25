using Assets.Editor.Models;
using System.ComponentModel;
using System.Windows;

namespace Assets.Editor.Views;

public partial class MirArchiveDialog : Window
{
    public MirArchiveDialogModel Model { get; }
    public MirArchiveDialog() : this(false) { }
    public MirArchiveDialog(bool convertToAsset)
    {
        Model = new MirArchiveDialogModel(convertToAsset) { Owner = this };
        DataContext = Model;
        InitializeComponent();
        Model.OnClose += (_, e) => e.Apply(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Model.RequestWindowClose()) e.Cancel = true;
        base.OnClosing(e);
    }
}
