using Assets.Editor.Common;
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
using System.Text.Json;
using System.Windows.Forms;
using System.Windows.Input;
using Xaml.Effects.Toolkit.Model;

namespace Assets.Editor.Models
{
    public class ExportSpriteFrameModel : DialogModel
    {
        public ICommand SelectSourceCommand { get; protected set; }

        public AssetFileStream stream { get; set; }

        public ExportSpriteFrameModel()
        {
            this.Title = "导出动画帧资源";
            this.ExportDirectory = "";
            this.Progress = 0;
            this.FrameLength = 4;
            this.FrameInterval = 0;
            this.SelectSourceCommand = new RelayCommand(SelectSource_Click);
        }


        JsonSerializerOptions jsonOptions = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };



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
            var index = 0;
            var count = stream.NumberOfFiles / (frameLength + frameInterval);
            for (int i = 0; i <= stream.NumberOfFiles - frameLength; i += (int)(frameLength + frameInterval))
            {
                var images = Enumerable.Range(i,(int)frameLength).Select(index => readImage(index));
                var result = packageImage(images.ToList());
                string json = JsonSerializer.Serialize(result.Frames, jsonOptions);
                File.WriteAllText(Path.Combine(this.ExportDirectory, index + ".json"), json);
                result.Image.Save(Path.Combine(this.ExportDirectory, index + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                index++;
                result.Image.Dispose();
                this.Progress = index / count;
            }
        }

        private SpriteFrameResult packageImage(List<ImageUnit> images)
        {
            // 横向一排打包
            var result = new SpriteFrameResult();
            var width = images.Sum(image => image.Image.Width);
            var height = images.Max(image => image.Image.Height);
            result.Image = new Bitmap(width, height);

            var frames = result.Frames;
            var x = 0;
            using (var g = Graphics.FromImage(result.Image))
            {
                foreach (var img in images)
                {
                    var frame = new SpriteFrame();
                    frame.X = x;
                    frame.Y = 0;
                    frame.Width = img.Image.Width;
                    frame.Height = img.Image.Height;
                    frame.OffsetX = img.OffsetX;
                    frame.OffsetY = img.OffsetY;
                    g.DrawImage(img.Image, new Point(x, 0));
                    frames.Add(frame);
                    x += img.Image.Width;
                    img.Image.Dispose();
                }
            }

            return result;
        }


        class ImageUnit
        {
            public Bitmap Image;
            public Int32 OffsetX;
            public Int32 OffsetY;

        }



        private ImageUnit readImage(int index)
        {
            var block = this.stream.Read((UInt32)index);
            if (block.lpType == ImageTypes.PNG)
            {
                AlphaUtil.SwitchRedBlue(block.Data);
            }
            var bitmap = new Bitmap(block.Width, block.Height);
            BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, block.Width, block.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(block.Data, 0, bmpData.Scan0, block.Data.Length);
            bitmap.UnlockBits(bmpData);
            return new ImageUnit()
            {
                Image = bitmap,
                OffsetX = block.OffsetX,
                OffsetY = block.OffsetY,
            };
        }








        public UInt32 FrameLength
        {
            get
            {
                return this.frameLength;
            }
            set
            {
                base.SetProperty(ref this.frameLength, value);
            }
        }
        private UInt32 frameLength;


        public UInt32 FrameInterval

        {
            get
            {
                return this.frameInterval;
            }
            set
            {
                base.SetProperty(ref this.frameInterval, value);
            }
        }
        private UInt32 frameInterval;

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
