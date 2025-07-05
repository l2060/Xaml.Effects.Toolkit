using Assets.Editor.Utils;
using Microsoft.Toolkit.Mvvm.Input;
using Resource.Package.Assets;
using Resource.Package.Assets.Common;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Xaml.Effects.Toolkit.Model;


namespace Assets.Editor.Models
{

    public class ImportDialogModel : DialogModel
    {
        public readonly String ImageFilter = "Image File|*.png;*.bmp;*.gif;*.jpg;*.tif;*.tga";
        public ICommand SelectSourceCommand { get; protected set; }
        public IRelayCommand ModeChangedCommand { get; protected set; }

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
            this.ModeChangedCommand = new RelayCommand<RoutedEventArgs>(ImportMode_Changed);
            this.SelectSourceCommand = new RelayCommand(SelectSource_Click);
        }


        private void SelectSource_Click()
        {
            if (this.IsBatch)
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


        private void ImportMode_Changed(RoutedEventArgs e)
        {
            this.ImportSource = "";
            this.SubmitCommand.NotifyCanExecuteChanged();
        }


        protected override bool Can_Submit()
        {
            return !string.IsNullOrEmpty(this.ImportSource);
        }

        /// <summary>
        /// 提交按钮事件
        /// </summary>
        protected override void Execute_Submit()
        {
            this.Progress = 0;
            List<String> files = new List<string>();
            var dir = "";
            if (this.IsBatch)
            {
                var fs = from file in Directory.EnumerateFiles(this.ImportSource, "*.*", SearchOption.TopDirectoryOnly) where FilterFile(file) select file;
                dir = this.ImportSource;
                files.AddRange(fs);
            }
            else
            {
                files.AddRange(this.ImportSource.Split("\n", StringSplitOptions.RemoveEmptyEntries));
                dir = Path.GetDirectoryName(files[0]);
            }
            var worker = new BackgroundWorker();
            worker.DoWork += (s, e2) =>
            {
                this.ImportResources(dir, files);
            };
            worker.RunWorkerCompleted += (s, e2) =>
            {
                this.StatusText = "Import Complete...";
                System.Windows.MessageBox.Show("导入完成");
                this.DialogResult = true;
            };
            worker.RunWorkerAsync();
        }



        private void ImportResources(String dirName, List<String> files)
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
            this.StatusText = "Loading Files Raw Data..";
            for (int i = 0; i < files.Count; i++)
            {
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
                this.Progress = (Double)i / (Double)fileCount * 100.0f;
            }
            this.StatusText = "Compressed Writeing File..";
            this.stream.Import(blocks, (value) =>
            {
                this.Progress = value;
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



        public Boolean IsBatch
        {
            get
            {
                return this.isBatch;
            }
            set
            {
                base.SetProperty(ref this.isBatch, value);
            }
        }

        private Boolean isBatch;


        public String ImportSource
        {
            get
            {
                return this.importSource;
            }
            set
            {
                base.SetProperty(ref this.importSource, value);
            }
        }

        private String importSource;







        public Double Progress
        {
            get
            {
                return this.progress;
            }
            set
            {
                base.SetPropertyAsync(ref this.progress, value);
            }
        }
        private Double progress;


        public String StatusText
        {
            get
            {
                return this.statusText;
            }
            set
            {
                base.SetPropertyAsync(ref this.statusText, value);
            }
        }

        private String statusText;


    }
}
