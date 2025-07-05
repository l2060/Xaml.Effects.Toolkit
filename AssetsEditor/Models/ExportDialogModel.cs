using Assets.Editor.Utils;
using Microsoft.Toolkit.Mvvm.Input;
using Resource.Package.Assets;
using Resource.Package.Assets.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Xaml.Effects.Toolkit.Model;


namespace Assets.Editor.Models
{

    public class ExportDialogModel : DialogModel
    {
        public readonly String ImageFilter = "Image File|*.png;*.bmp;*.gif;*.jpg;*.tif;*.tga";
        public ICommand SelectSourceCommand { get; protected set; }
        public IRelayCommand ModeChangedCommand { get; protected set; }

        public AssetFileStream stream { get; set; }



        public ExportDialogModel()
        {
            this.Title = "导出资源";
            this.ExportDirectory = "";
            this.IsBatch = false;
            this.Progress = 0;
            this.StartIndex = 0;
            this.EndIndex = 0;
            this.ImportOptions = ImageUserData.SchemaJson;
            this.ModeChangedCommand = new RelayCommand<RoutedEventArgs>(ImportMode_Changed);
            this.SelectSourceCommand = new RelayCommand(SelectSource_Click);
        }


        private void SelectSource_Click()
        {
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.Description = "选择最终目录";
            folderBrowserDialog.ShowNewFolderButton = true;
            folderBrowserDialog.InitialDirectory = ConfigureUtil.GetValue("Export-Directory");
            folderBrowserDialog.RootFolder = Environment.SpecialFolder.Desktop;
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ConfigureUtil.SetValue("Export-Directory", folderBrowserDialog.SelectedPath);
                this.ExportDirectory = folderBrowserDialog.SelectedPath;
            }
            ;
            this.Progress = 0;
            this.SubmitCommand.NotifyCanExecuteChanged();
        }




        private void ImportMode_Changed(RoutedEventArgs e)
        {
            if (this.IsBatch)
            {
                this.EndIndex = this.stream.NumberOfFiles - 1;
            }
            else
            {
                this.EndIndex = 1;
            }
            this.SubmitCommand.NotifyCanExecuteChanged();
        }


        protected override bool Can_Submit()
        {
            return !string.IsNullOrEmpty(this.ExportDirectory);
        }

        /// <summary>
        /// 提交按钮事件
        /// </summary>
        protected override void Execute_Submit()
        {
            var worker = new BackgroundWorker();
            worker.DoWork += this.Export;
            worker.RunWorkerCompleted += (s, e2) =>
            {
                System.Windows.MessageBox.Show("导出完成");
                this.DialogResult = true;
            };
            worker.RunWorkerAsync();
        }


        private void Export(object? sender, DoWorkEventArgs e)
        {
            this.Progress = 0;
            List<String> files = new List<string>();
            Dictionary<String, DataInfo> schemas = new Dictionary<String, DataInfo>();
            if (ImportOptions == ImageUserData.Placements)
            {
                Directory.CreateDirectory(Path.Combine(this.ExportDirectory, "Placements"));
            }
            else if (ImportOptions == ImageUserData.SchemaJson)
            {
                var pname = Path.Combine(this.ExportDirectory, $"schema.json");
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
            for (uint i = this.StartIndex; i <= this.EndIndex; i++)
            {
                var block = this.stream.Read((UInt32)i);
                var fileName = i.ToString().PadLeft(7, '0');
                var extName = this.GetFileName(block.lpType);
                var outFileName = Path.Join(this.ExportDirectory, fileName + extName);

                if (ImportOptions == ImageUserData.SchemaJson)
                {
                    schemas[fileName + extName] = new DataInfo()
                    {
                        lpRenderType = block.lpRenderType,
                        OffsetX = block.OffsetX,
                        OffsetY = block.OffsetY,
                        Unknown1 = block.Unknown1,
                        Unknown2 = block.Unknown2,
                    };
                }
                else if (ImportOptions == ImageUserData.Placements)
                {
                    var f = Path.Combine(this.ExportDirectory, $"Placements\\{fileName}.txt");
                    File.WriteAllLines(f, new String[] { block.OffsetX.ToString(), block.OffsetY.ToString() });
                }

                if (block.Data.Length == 0)
                {
                    File.WriteAllBytes(outFileName, BitmapUtil.EmptyBitmapData);
                }
                else
                {
                    try
                    {
                        if (block.lpType == ImageTypes.PNG)
                        {
                            AlphaUtil.UnpremultiplyAlpha(block.Data);
                        }
                        // 需要转换红蓝通道
                        AlphaUtil.SwitchRedBlue(block.Data);
                        System.Drawing.Imaging.ImageFormat outFormat = BitmapUtil.GetOutFileFormat(block.lpType);
                        using (var bitmap = new Bitmap(block.Width, block.Height))
                        {
                            BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, block.Width, block.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                            Marshal.Copy(block.Data, 0, bmpData.Scan0, block.Data.Length);
                            bitmap.UnlockBits(bmpData);
                            bitmap.Save(outFileName, outFormat);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }

                if (i - this.StartIndex == this.EndIndex - this.StartIndex)
                {
                    this.Progress = 100.0f;
                }
                else
                {
                    this.Progress = (Double)(i - this.StartIndex) / (Double)(this.EndIndex - this.StartIndex) * 100.0f;
                }
            }

            if (ImportOptions == ImageUserData.SchemaJson)
            {
                JsonSerializerOptions options = new JsonSerializerOptions();
                //设置支持中文的unicode编码
                options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
                //启用驼峰格式
                options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                //启用缩进设置
                options.WriteIndented = true;
                schemas = this.SortDictionary(schemas);
                string json = JsonSerializer.Serialize(schemas, options);
                File.WriteAllText(Path.Combine(this.ExportDirectory, "schema.json"), json);
            }
        }


        private Dictionary<String, DataInfo> SortDictionary(Dictionary<String, DataInfo> pairs)
        {
            var keys = pairs.Keys.ToArray();
            Array.Sort(keys);
            var newScheamas = new Dictionary<String, DataInfo>();
            foreach (var key in keys)
            {
                newScheamas[key] = pairs[key];
            }
            return newScheamas;
        }



        private string GetFileName(ImageTypes types)
        {
            if (types == ImageTypes.BMP) return ".bmp";
            if (types == ImageTypes.TGA) return ".tga";
            if (types == ImageTypes.TIFF) return ".tif";
            if (types == ImageTypes.PNG) return ".png";
            if (types == ImageTypes.GIF) return ".gif";
            if (types == ImageTypes.JPG) return ".jpg";
            return ".png";
        }



        public UInt32 StartIndex
        {
            get
            {
                return this.startIndex;
            }
            set
            {
                base.SetProperty(ref this.startIndex, value);
            }
        }
        private UInt32 startIndex;


        public UInt32 EndIndex

        {
            get
            {
                return this.endIndex;
            }
            set
            {
                base.SetProperty(ref this.endIndex, value);
            }
        }
        private UInt32 endIndex;

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



        public ImageUserData ImportOptions
        {
            get
            {
                return this.importOptions;
            }
            set
            {
                base.SetProperty(ref this.importOptions, value);
            }
        }
        private ImageUserData importOptions;



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


        public String ExportDirectory
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

    }
}
