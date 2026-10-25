using Assets.Editor.Utils;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Win32;
using Mir2ResourceParser;
using System;
using System.IO;
using System.Windows;

namespace Assets.Editor.Models;

public sealed class MirArchiveDialogModel : ResourceOperationModel
{
    private string sourcePath = "", outputPath = "", palettePath = "", password = "";
    private bool generateAlpha, compress = true;
    internal Window Owner { get; set; }
    public bool IsAssetMode { get; }
    public string OutputLabel => IsAssetMode ? "输出文件：" : "输出目录：";
    public string ActionText => IsAssetMode ? "开始转换" : "开始解包";
    public string OutputHint => IsAssetMode
        ? "保留原始帧编号、空帧和偏移，可直接用资源编辑器打开。"
        : "在所选目录下创建同名文件夹，输出 PNG 和 schema.json；空帧保留透明占位图。";
    public IRelayCommand SelectSourceCommand { get; }
    public IRelayCommand SelectOutputCommand { get; }
    public IRelayCommand SelectPaletteCommand { get; }

    public MirArchiveDialogModel(bool assetMode)
    {
        IsAssetMode = assetMode;
        Title = assetMode ? "Wil/Wzl转asset" : "Wil/Wzl解包";
        SelectSourceCommand = new RelayCommand(SelectSource);
        SelectOutputCommand = new RelayCommand(SelectOutput);
        SelectPaletteCommand = new RelayCommand(SelectPalette);
    }

    public string SourcePath
    {
        get => sourcePath;
        set { SetProperty(ref sourcePath, value); SubmitCommand.NotifyCanExecuteChanged(); }
    }
    public string OutputPath
    {
        get => outputPath;
        set { SetProperty(ref outputPath, value); SubmitCommand.NotifyCanExecuteChanged(); }
    }
    public string PalettePath { get => palettePath; set => SetProperty(ref palettePath, value); }
    public string Password { get => password; set => SetProperty(ref password, value); }
    public bool GenerateAlpha { get => generateAlpha; set => SetProperty(ref generateAlpha, value); }
    public bool Compress { get => compress; set => SetProperty(ref compress, value); }

    private void SelectSource()
    {
        var dialog = new OpenFileDialog { Title = "选择 Wil/Wzl 资源包", Filter = MirArchiveOperations.FileFilter };
        if (dialog.ShowDialog(Owner) != true) return;
        SourcePath = dialog.FileName;
        if (string.IsNullOrWhiteSpace(OutputPath))
            OutputPath = IsAssetMode ? Path.ChangeExtension(SourcePath, ".asset") : Path.GetDirectoryName(SourcePath);
    }

    private void SelectOutput()
    {
        if (IsAssetMode)
        {
            var dialog = new SaveFileDialog
            {
                Title = "保存 asset 资源包", Filter = "Asset 资源包|*.asset", DefaultExt = ".asset",
                FileName = string.IsNullOrWhiteSpace(SourcePath) ? "resources.asset" : Path.ChangeExtension(Path.GetFileName(SourcePath), ".asset"),
                OverwritePrompt = false // Confirm once, against the final path when starting the job.
            };
            if (dialog.ShowDialog(Owner) == true) OutputPath = dialog.FileName;
        }
        else
        {
            var dialog = new OpenFolderDialog { Title = "选择解包输出目录" };
            if (dialog.ShowDialog(Owner) == true) OutputPath = dialog.FolderName;
        }
    }

    private void SelectPalette()
    {
        var dialog = new OpenFileDialog { Title = "选择调色板（768 字节 RGB 或 1024 字节 BGRA）", Filter = "调色板|*.pal|所有文件|*.*" };
        if (dialog.ShowDialog(Owner) == true) PalettePath = dialog.FileName;
    }

    protected override bool Can_Submit() => !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);

    protected override async void Execute_Submit()
    {
        if (!Can_Submit()) return;
        try
        {
            string source = Path.GetFullPath(SourcePath.Trim());
            string output = Path.GetFullPath(OutputPath.Trim());
            string palette = string.IsNullOrWhiteSpace(PalettePath) ? null : Path.GetFullPath(PalettePath.Trim());
            string pass = Password;
            bool compression = Compress;
            var alpha = GenerateAlpha ? AlphaMode.FromBlackBackground : AlphaMode.Preserve;
            bool overwrite = IsAssetMode && File.Exists(output);
            if (overwrite && MessageBox.Show(Owner, $"转换完成后将替换以下文件：\n{output}", "替换资源包",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            string destination = IsAssetMode ? output : MirArchiveOperations.ExtractionPath(source, output);
            await RunOperationAsync((progress, token) =>
            {
                if (IsAssetMode) MirArchiveOperations.CreateAsset(source, output, pass, compression, palette, alpha, overwrite, progress, token);
                else MirArchiveOperations.Extract(source, output, palette, alpha, progress, token);
            }, $"{(IsAssetMode ? "转换" : "解包")}完成：{destination}");
        }
        catch (Exception ex) { StatusText = $"无法开始：{ex.Message}"; }
    }
}
