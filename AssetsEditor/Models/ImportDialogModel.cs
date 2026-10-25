using Assets.Editor.Utils;
using Microsoft.Toolkit.Mvvm.Input;
using Resource.Package.Assets;
using Resource.Package.Assets.Common;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;


namespace Assets.Editor.Models
{

    public enum ImportSourceMode { Files, Directory, MirArchive }

    public class ImportDialogModel : ResourceOperationModel
    {
        public readonly String ImageFilter = "Image File|*.png;*.bmp;*.gif;*.jpg;*.tif;*.tga";
        public ICommand SelectSourceCommand { get; protected set; }
        public bool HasImportedResources => HasChanges;
        protected override string CancellationMessage => "已取消；已写入的资源会保留。";

        public AssetFileStream stream { get; set; }

        public UInt32 SelectedIndex { get; set; }

        public ImportDialogModel()
        {
            this.Title = "导入资源";
            this.ImportSource = "";
            this.IsBatch = false;
            this.Progress = 0;
            this.RenderType = RenderTypes.Normal;
            this.FormatOptions = ImageFormat.ALLIMAGE;
            this.ImportUserData = ImageUserData.None;
            this.ImportOption = ImportOption.Append;
            this.SelectSourceCommand = new RelayCommand(SelectSource_Click);
            this.StatusText = "请选择要导入的资源。";
        }


        private void SelectSource_Click()
        {
            if (IsMirMode)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择 Wil/Wzl 资源包", Filter = MirArchiveOperations.FileFilter,
                    Multiselect = false
                };
                if (dialog.ShowDialog() == true) ImportSource = dialog.FileName;
            }
            else if (this.IsBatch)
            {
                FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
                folderBrowserDialog.Description = "选择最终目录";
                folderBrowserDialog.ShowNewFolderButton = true;
                folderBrowserDialog.InitialDirectory = ConfigureUtil.GetValue("ImportDirectory");
                folderBrowserDialog.RootFolder = Environment.SpecialFolder.Desktop;
                if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    ConfigureUtil.SetValue("ImportDirectory", folderBrowserDialog.SelectedPath);
                    this.ImportSource = folderBrowserDialog.SelectedPath;
                    if (Directory.Exists(Path.Combine(this.ImportSource, "Placements")))
                    {
                        this.ImportUserData = ImageUserData.Placements;
                    }
                    else if (File.Exists(Path.Combine(this.ImportSource, "schema.json")))
                    {
                        this.ImportUserData = ImageUserData.SchemaJson;
                    }
                    else
                    {
                        this.ImportUserData = ImageUserData.None;
                    }
                }
                ;
            }
            else
            {
                Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
                ofd.Title = "选择图片文件";
                ofd.InitialDirectory = ConfigureUtil.GetValue("ImportFile");
                ofd.Filter = ImageFilter;
                ofd.Multiselect = true;
                if (ofd.ShowDialog() == true)
                {
                    var dir = Path.GetDirectoryName(ofd.FileName);
                    ConfigureUtil.SetValue("ImportFile", dir);
                    this.ImportSource = String.Join("\n", ofd.FileNames);

                    if (Directory.Exists(Path.Combine(dir, "Placements")))
                    {
                        this.ImportUserData = ImageUserData.Placements;
                    }
                    else if (File.Exists(Path.Combine(dir, "schema.json")))
                    {
                        this.ImportUserData = ImageUserData.SchemaJson;
                    }
                    else
                    {
                        this.ImportUserData = ImageUserData.None;
                    }
                }
            }
            this.Progress = 0;
            this.SubmitCommand.NotifyCanExecuteChanged();
        }


        public void SetSourceFiles(String[] files)
        {
            SourceMode = ImportSourceMode.Files;
            if (files.Length == 0) { ImportSource = ""; return; }
            this.ImportSource = String.Join("\n", files);
            var dir = Path.GetDirectoryName(files[0]);
            if (Directory.Exists(Path.Combine(dir, "Placements")))
            {
                this.ImportUserData = ImageUserData.Placements;
            }
            else if (File.Exists(Path.Combine(dir, "schema.json")))
            {
                this.ImportUserData = ImageUserData.SchemaJson;
            }
            else
            {
                this.ImportUserData = ImageUserData.None;
            }
            this.Progress = 0;
            this.SubmitCommand.NotifyCanExecuteChanged();
        }




        protected override bool Can_Submit()
        {
            return !IsBusy && stream != null && !string.IsNullOrWhiteSpace(this.ImportSource);
        }

        /// <summary>
        /// 提交按钮事件
        /// </summary>
        protected override async void Execute_Submit()
        {
            if (!Can_Submit()) return;
            string source = ImportSource;
            var mode = SourceMode;
            // Each batch is committed by the existing writer; refresh after cancellation/failure too.
            HasChanges = true;
            await RunOperationAsync((progress, token) =>
            {
                if (mode == ImportSourceMode.MirArchive)
                {
                    int? start = ImportOption switch
                    {
                        ImportOption.Append => null,
                        ImportOption.Override => 0,
                        _ => checked((int)SelectedIndex)
                    };
                    MirArchiveOperations.ImportToAsset(source, stream, start, progress, token);
                }
                else
                {
                    var files = new List<string>();
                    string dir;
                    if (mode == ImportSourceMode.Directory)
                    {
                        dir = source;
                        var scan = IncludeChildDir ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                        foreach (string file in Directory.EnumerateFiles(source, "*.*", scan))
                        {
                            token.ThrowIfCancellationRequested();
                            if (FilterFile(file)) files.Add(file);
                        }
                        files.Sort(StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        files.AddRange(source.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        dir = Path.GetDirectoryName(files[0]);
                    }
                    if (files.Count == 0) throw new InvalidOperationException("没有找到符合条件的图片。");
                    ImportResources(dir, files, progress, token);
                }
            }, "导入完成。", closeOnSuccess: true);
        }



        private void ImportResources(String dirName, List<String> files, IProgress<ResourceOperationProgress> progress, CancellationToken token)
        {
            List<DataBlock> blocks = new List<DataBlock>();
            var fileCount = files.Count;
            Dictionary<String, DataInfo> schemas = new Dictionary<String, DataInfo>();
            if (this.ImportUserData == ImageUserData.SchemaJson)
            {
                var pname = Path.Combine(dirName, $"schema.json");
                if (System.IO.File.Exists(pname))
                {
                    var placements = System.IO.File.ReadAllText(pname);
                    JsonSerializerOptions options = new JsonSerializerOptions();
                    //设置支持中文的unicode编码
                    options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
                    //启用驼峰格式
                    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    //启用缩进设置
                    options.WriteIndented = true;
                    schemas = JsonSerializer.Deserialize<Dictionary<String, DataInfo>>(placements, options);
                }
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var file = files[i];
                var block = new DataBlock();
                if (file.EndsWith(".bmp"))
                {
                    block.lpRenderType = this.RenderType;
                }
                else
                {
                    block.lpRenderType = RenderTypes.Normal;
                }

                var bytes = File.ReadAllBytes(file);
                block.lpType = BitmapUtil.ParseImageFormat(bytes);
                ImageResult imageResult = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
                if (imageResult.Width > 1 || imageResult.Height > 1)
                {
                    // Png 图片需要处理Alpha预乘
                    if (block.lpType == ImageTypes.PNG) AlphaUtil.PremultiplyAlpha(imageResult.Data);
                    block.Width = imageResult.Width;
                    block.Height = imageResult.Height;
                    block.Data = imageResult.Data;
                }
                else
                {
                    block.lpType = ImageTypes.Unknown;
                }

                var filename = Path.GetFileNameWithoutExtension(file);
                if (this.ImportUserData == ImageUserData.Placements)
                {
                    var dirname = Path.GetDirectoryName(file);
                    var pname = Path.Combine(dirname, $"Placements\\{filename}.txt");
                    if (System.IO.File.Exists(pname))
                    {
                        var placements = System.IO.File.ReadAllLines(pname);
                        block.OffsetX = Int16.Parse(placements[0]);
                        block.OffsetY = Int16.Parse(placements[1]);
                    }
                }
                else if (this.ImportUserData == ImageUserData.SchemaJson)
                {
                    var _filename = Path.GetFileName(file);
                    if (schemas.TryGetValue(_filename, out var schema))
                    {
                        block.OffsetX = schema.OffsetX;
                        block.OffsetY = schema.OffsetY;
                        block.lpRenderType = schema.lpRenderType;
                        block.Unknown1 = schema.Unknown1;
                        block.Unknown2 = schema.Unknown2;
                    }
                }
                if (this.ImportOption == ImportOption.Override)
                {
                    block.Index = (Int32)UInt32.Parse(filename);
                }
                else if (this.ImportOption == ImportOption.Append)
                {
                    block.Index = -1;
                }
                else if (this.ImportOption == ImportOption.Replace)
                {
                    block.Index = (Int32)this.SelectedIndex + i;
                }
                blocks.Add(block);
                if (timer.ElapsedMilliseconds >= 80 || i + 1 == fileCount)
                {
                    progress.Report(new(i + 1, fileCount * 2, $"正在读取图片：{Path.GetFileName(file)}"));
                    timer.Restart();
                }
            }
            token.ThrowIfCancellationRequested();
            progress.Report(new(fileCount, fileCount * 2, "正在压缩并写入资源…"));
            int reported = -1;
            this.stream.Import(blocks, (value) =>
            {
                int percent = (int)value;
                int previous;
                do
                {
                    previous = Volatile.Read(ref reported);
                    if (percent <= previous) return;
                } while (Interlocked.CompareExchange(ref reported, percent, previous) != previous);
                progress.Report(new(fileCount + (int)(fileCount * value / 100), fileCount * 2, "正在压缩并写入资源…"));
            });
        }


        private Boolean FilterFile(string filename)
        {
            var ext = Path.GetExtension(filename).ToLower();
            if (this.FormatOptions == ImageFormat.ALLIMAGE) return ".bmp|.jpg|.gif|.png|.tiff|.tga".Contains(ext);
            if ((this.FormatOptions | ImageFormat.BMP) == ImageFormat.BMP) return ext == ".bmp";
            if ((this.FormatOptions | ImageFormat.JPG) == ImageFormat.JPG) return ext == ".jpg";
            if ((this.FormatOptions | ImageFormat.GIF) == ImageFormat.GIF) return ext == ".gif";
            if ((this.FormatOptions | ImageFormat.PNG) == ImageFormat.PNG) return ext == ".png";
            if ((this.FormatOptions | ImageFormat.TIFF) == ImageFormat.TIFF) return ext == ".tif";
            if ((this.FormatOptions | ImageFormat.TGA) == ImageFormat.TGA) return ext == ".tga";
            return false;
        }









        public RenderTypes RenderType
        {
            get
            {
                return this.renderType;
            }
            set
            {
                base.SetProperty(ref this.renderType, value);
            }
        }
        private RenderTypes renderType;






        public ImageFormat FormatOptions
        {
            get
            {
                return this.formatOptions;
            }
            set
            {
                base.SetProperty(ref this.formatOptions, value);
            }
        }
        private ImageFormat formatOptions;






        public ImportOption ImportOption
        {
            get
            {
                return this.importOption;
            }
            set
            {
                base.SetProperty(ref this.importOption, value);
            }
        }
        private ImportOption importOption;


        public ImageUserData ImportUserData
        {
            get
            {
                return this.importUserData;
            }
            set
            {
                base.SetProperty(ref this.importUserData, value);
            }
        }
        private ImageUserData importUserData;



        private ImportSourceMode sourceMode;
        public ImportSourceMode SourceMode
        {
            get => sourceMode;
            set
            {
                if (!SetProperty(ref sourceMode, value)) return;
                OnPropertyChanged(nameof(IsFileMode));
                OnPropertyChanged(nameof(IsBatch));
                OnPropertyChanged(nameof(IsMirMode));
                OnPropertyChanged(nameof(CanUseImageOptions));
                if (!IsBatch) IncludeChildDir = false;
                if (IsMirMode) ImportUserData = ImageUserData.None;
                ImportSource = "";
                Progress = 0;
                StatusText = IsMirMode
                    ? "需配套 WIX/WZX；保留帧编号、空帧和偏移。取消时保留已导入的批次。"
                    : "请选择要导入的资源。";
            }
        }
        public bool IsFileMode { get => SourceMode == ImportSourceMode.Files; set { if (value) SourceMode = ImportSourceMode.Files; } }
        public bool IsBatch { get => SourceMode == ImportSourceMode.Directory; set { if (value) SourceMode = ImportSourceMode.Directory; else if (IsBatch) SourceMode = ImportSourceMode.Files; } }
        public bool IsMirMode { get => SourceMode == ImportSourceMode.MirArchive; set { if (value) SourceMode = ImportSourceMode.MirArchive; } }
        public bool CanUseImageOptions => !IsMirMode;


        public String ImportSource
        {
            get
            {
                return this.importSource;
            }
            set
            {
                base.SetProperty(ref this.importSource, value);
                this.SubmitCommand.NotifyCanExecuteChanged();
            }
        }

        private String importSource;





        public Boolean IncludeChildDir
        {
            get
            {
                return this.includeChildDir;
            }
            set
            {
                base.SetProperty(ref this.includeChildDir, value);
            }
        }

        private Boolean includeChildDir;




        






    }
}
