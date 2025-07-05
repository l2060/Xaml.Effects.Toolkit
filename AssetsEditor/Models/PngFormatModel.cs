using Assets.Editor.Common;
using Assets.Editor.Utils;
using Microsoft.Toolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using Xaml.Effects.Toolkit.Model;


namespace Assets.Editor.Models
{

    public class PngFormatModel : DialogModel
    {
        public ICommand SelectSourceCommand { get; protected set; }
        public PngFormatModel()
        {
            this.Title = "Png标准化工具";
            this.Directory = "";
            this.DrawingMode = DrawingMode.MaskColor;
            this.SelectSourceCommand = new RelayCommand(SelectSource_Click);
            this.Progress = 0;
        }


        protected override bool Can_Submit()
        {
            return !string.IsNullOrEmpty(this.Directory);
        }

        /// <summary>
        /// 提交按钮事件
        /// </summary>
        protected override void Execute_Submit()
        {
            var worker = new BackgroundWorker();
            worker.DoWork += Convert_DoWork;
            worker.RunWorkerCompleted += (s, e2) =>
            {
                System.Windows.MessageBox.Show("格式化完成");
                this.DialogResult = true;
            };
            worker.RunWorkerAsync();
        }

        private void Convert_DoWork(object sender, DoWorkEventArgs e)
        {
            this.Progress = 0;
            var files = (from file in System.IO.Directory.EnumerateFiles(this.Directory, "*.png", System.IO.SearchOption.TopDirectoryOnly) select file).ToList();
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                try
                {
                    var bytes = File.ReadAllBytes(file);
                    using (var stream = new MemoryStream(bytes))
                    {
                        using (var bitmap = new System.Drawing.Bitmap(stream))
                        {
                            bitmap.Save(file);
                        }
                    }
                    this.Progress = (Double)i / files.Count * 100.0f;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"{file} 格式化失败：{ex.Message}");
                }
            }
            this.Progress = 100;
        }


        private void SelectSource_Click()
        {
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.Description = "选择转换目录";
            folderBrowserDialog.ShowNewFolderButton = true;
            folderBrowserDialog.InitialDirectory = ConfigureUtil.GetValue("PngFormatDirectory");
            folderBrowserDialog.RootFolder = Environment.SpecialFolder.Desktop;
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ConfigureUtil.SetValue("Bmp2Png-Directory", folderBrowserDialog.SelectedPath);
                this.Directory = folderBrowserDialog.SelectedPath;
            }
            ;
            this.SubmitCommand.NotifyCanExecuteChanged();
        }


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



        public DrawingMode DrawingMode
        {
            get
            {
                return this.drawingMode;
            }
            set
            {
                base.SetProperty(ref this.drawingMode, value);
            }
        }

        private DrawingMode drawingMode;


        public String Directory
        {
            get
            {
                return this.directory;
            }
            set
            {
                base.SetProperty(ref this.directory, value);
            }
        }

        private String directory;

    }
}
