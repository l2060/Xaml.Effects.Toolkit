using Assets.Editor.Common;
using Assets.Editor.Utils;
using Assets.Editor.Views;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Win32;
using Resource.Package.Assets;
using Resource.Package.Assets.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xaml.Effects.Toolkit;
using Xaml.Effects.Toolkit.Behaviors;
using Xaml.Effects.Toolkit.Model;

namespace Assets.Editor.Models
{

    public enum ViewGrids
    {
        [Description("人物阵列(8*4)")]
        GRID_8X4,
        [Description("怪物阵列(10*4)")]
        GRID_10X4,
        [Description("其他阵列(12*4)")]
        GRID_12X4,
        [Description("密集阵列(16*4)")]
        GRID_16X4,
        [Description("密集阵列(20*4)")]
        GRID_20X4


    }




    public class MainWindowModel : DialogModel
    {
        public readonly String ImageFilter = "Image File|*.png;*.bmp;*.gif;*.jpg;*.tif";

        public ObservableCollection<String> ListItems { get; set; } = new ObservableCollection<String>();

        public ObservableCollection<ImageModel> GridImages { get; set; } = new ObservableCollection<ImageModel>();


        public ICommand PreviewMouseWheelCommand { get; protected set; }

        public ICommand PageChangedCommand { get; protected set; }

        public ICommand SelectionChangedCommand { get; protected set; }

        public ICommand OffsetChangedCommand { get; protected set; }
        public IRelayCommand OffsetCommitCommand { get; protected set; }

        public IRelayCommand BatchOffsetCommitCommand { get; protected set; }

        public ICommand RenderTypeChangedCommand { get; protected set; }

        public ICommand ListGridChangedCommand { get; protected set; }

        public ICommand NewPackageCommand { get; protected set; }

        public ICommand OpenPackageCommand { get; protected set; }

        public IRelayCommand ClosePackageCommand { get; protected set; }

        public IRelayCommand ChangePasswordCommand { get; protected set; }

        public IRelayCommand ImportImageCommand { get; protected set; }

        public IRelayCommand CleanImageCommand { get; protected set; }
        public IRelayCommand ExportImageCommand { get; protected set; }
        public IRelayCommand ExpandCommand { get; protected set; }
        public IRelayCommand RecycleCommand { get; protected set; }


        public IRelayCommand ButtonBuildCommand { get; protected set; }


        public IRelayCommand RegFileTypeCommand { get; protected set; }


        public IRelayCommand Bmp2PngCommand { get; protected set; }

        public IRelayCommand PngFormatCommand { get; protected set; }

        public IRelayCommand BatchImageOptimizeCommand { get; protected set; }

        public IRelayCommand BatchOffsetOptimizeCommand { get; protected set; }


        


        public ICommand ThemesCommand { get; protected set; }

        public AssetFileStream assetFile { get; protected set; }



        public IEventCommand ImageDropEventCommand { get; protected set; }
        public IEventCommand ImageDragEnterEventCommand { get; protected set; }


        public MainWindowModel()
        {
            ThemeManager.LoadThemeFromResource("/Assets.Editor;component/Assets/Themes/Black.xaml");
            this.ThemesCommand = new RelayCommand(Themes_Click);
            this.OpenPackageCommand = new RelayCommand(OpenPackage_Click);
            this.ClosePackageCommand = new RelayCommand(ClosePackage_Click, AssetFileOpened_CanClick);
            this.ImportImageCommand = new RelayCommand(ImportImage_Click, AssetFileOpened_CanClick);
            this.ExportImageCommand = new RelayCommand(ExportImage_Click, AssetFileOpened_CanClick);
            this.CleanImageCommand = new RelayCommand(CleanImage_Click, CleanImage_CanClick);

            this.ExpandCommand = new RelayCommand(Expand_Click, AssetFileOpened_CanClick);
            this.RecycleCommand = new RelayCommand(Recycle_Click, AssetFileOpened_CanClick);
            this.BatchImageOptimizeCommand = new RelayCommand(BatchImageOptimize_Click, AssetFileOpened_CanClick);
            this.BatchOffsetOptimizeCommand = new RelayCommand(BatchOffsetOptimize_Click, AssetFileOpened_CanClick);

            this.ChangePasswordCommand = new RelayCommand(ChangePassword_Click, AssetFileOpened_CanClick);
            this.PreviewMouseWheelCommand = new RelayCommand<MouseWheelEventArgs>(ListView_PreviewMouseWheel);
            this.PageChangedCommand = new RelayCommand<RoutedPropertyChangedEventArgs<double>>(ScrollBar_ValueChanged);
            this.SelectionChangedCommand = new RelayCommand<SelectionChangedEventArgs>(ListView_SelectionChanged);
            this.OffsetChangedCommand = new RelayCommand<TextChangedEventArgs>(Offset_TextChanged);
            this.RenderTypeChangedCommand = new RelayCommand<SelectionChangedEventArgs>(RenderType_SelectionChanged);
            this.ListGridChangedCommand = new RelayCommand<SelectionChangedEventArgs>(ListGrid_SelectionChanged);
            this.OffsetCommitCommand = new RelayCommand(OffsetCommit_Click, OffsetCommit_CanClick);
            this.BatchOffsetCommitCommand = new RelayCommand(BatchOffsetCommit_Click, OffsetCommit_CanClick);
            this.NewPackageCommand = new RelayCommand(NewPackage_Click);
            this.RegFileTypeCommand = new RelayCommand(RegFileType_Click);
            this.Bmp2PngCommand = new RelayCommand(Bmp2Png_Click);
            this.PngFormatCommand = new RelayCommand(PngFormat_Click);

            this.ButtonBuildCommand = new RelayCommand(ButtonBuild_Click);
            this.ImageDragEnterEventCommand = new EventCommand<DragEventArgs>(Image_DragEnter);
            this.ImageDropEventCommand = new EventCommand<DragEventArgs>(Image_Drop);
            this.currentPage = 0;
            this.Title = "Assets Editor - Power by Hanks";
            this.PageSize = 64;
            this.ZoomValue = 1;
            this.DrawingMode = DrawingMode.Raw;
            this.IsRegFileType = FileTypeRegister.FileTypeRegistered(".Asset");
            this.Selected = new ImageModel();
            this.ListGrid = ViewGrids.GRID_16X4;
            this.ZeroLine = true;
            this.BoundsLine = true;

            resizePage();
            refreshPage();
        }


        private void Image_DragEnter(FrameworkElement sender, DragEventArgs e)
        {

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as String[];
                if (VerificationFiles(files))
                {
                    e.Effects = DragDropEffects.Link;
                    return;
                }
            }
            e.Effects = DragDropEffects.None;
        }

        public void Image_Drop(FrameworkElement sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as String[];
            if (!VerificationFiles(files)) return;
            Array.Sort(files);
            var model = sender.DataContext as ImageModel;


            ImportDialog import = new ImportDialog();
            import.Model.SelectedIndex = model.Index;
            import.Model.FormatOptions = ImageFormat.ALLIMAGE;
            import.Model.ImportOption = ImportOption.Replace;
            import.Model.stream = this.assetFile;
            import.Owner = MainWindow.Instance;
            import.Model.SetSourceFiles(files);

            var result = import.ShowDialog();
            if (result.HasValue && result.Value)
            {
                resizePage();
                refreshPage();
            }
        }

        private Boolean VerificationFiles(String[] files)
        {
            var exts = new String[] { ".png", ".jpg", ".bmp" };
            return files.Select(e =>
            {
                if (File.Exists(e))
                {
                    var ext = "*" + Path.GetExtension(e).ToLower();
                    return ImageFilter.IndexOf(ext) > -1;
                }
                return false;
            }).All(e => e == true);
        }




        private void NewPackage_Click()
        {
            var dialog = new CreateAssetsInput();
            dialog.Owner = MainWindow.Instance;
            var result = dialog.ShowDialog();
            if (result.HasValue && result.Value)
            {
                this.OpenAssetPackage(dialog.Model.FileName, dialog.Model.Password);
            }
        }

        private unsafe void ButtonBuild_Click()
        {
            OpenFileDialog ofd = new OpenFileDialog()
            {
                InitialDirectory = ConfigureUtil.GetValue("OpenAssetDirectory"),
                DereferenceLinks = false,
            };
            //ofd.InitialDirectory = @"D:\";
            ofd.Filter = "图片文件|*.png";
            if (ofd.ShowDialog() == true)
            {
                using (var fs = File.Open(ofd.FileName, FileMode.Open, FileAccess.ReadWrite))
                {
                    Bitmap bitmap = new Bitmap(fs);
                    var hover = this.AdjustBrightness(bitmap, 1.3f);
                    var pressed = this.AdjustBrightness(bitmap, 0.7f);
                    Bitmap outBitmap = new Bitmap(bitmap.Width, bitmap.Height * 3);
                    using (Graphics g = Graphics.FromImage(outBitmap))
                    {
                        g.DrawImage(bitmap, 0, 0);
                        g.DrawImage(hover, 0, bitmap.Height);
                        g.DrawImage(pressed, 0, bitmap.Height * 2);
                    }
                    ;
                    var dir = Path.GetDirectoryName(ofd.FileName);
                    var filename = Path.GetFileNameWithoutExtension(ofd.FileName);
                    var ext = Path.GetExtension(ofd.FileName);
                    outBitmap.Save($"{dir}\\{filename}_button{ext}", System.Drawing.Imaging.ImageFormat.Png);
                    bitmap.Dispose();
                    MessageBox.Show("按钮纹理已生成至文件所在目录", "按钮生成");
                }
                ;
            }
        }
        private Bitmap AdjustBrightness(Bitmap image, float factor)
        {
            Bitmap adjustedImage = new Bitmap(image.Width, image.Height);

            using (Graphics g = Graphics.FromImage(adjustedImage))
            {
                float brightness = Math.Max(0, Math.Min(2, factor)); // 确保亮度在 0 到 2 之间

                ImageAttributes attributes = new ImageAttributes();
                ColorMatrix colorMatrix = new ColorMatrix(new float[][]
                {
                new float[] { brightness, 0, 0, 0, 0 },
                new float[] { 0, brightness, 0, 0, 0 },
                new float[] { 0, 0, brightness, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 }
                });
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }

            return adjustedImage;
        }

        private void BatchImageOptimize_Click()
        {
            var dialog = new BatchOptimizeDialog();
            dialog.Model.stream = this.assetFile;
            dialog.Model.StartIndex = this.Selected.Index;
            dialog.Model.EndIndex = this.assetFile.NumberOfFiles - 1;
            dialog.Owner = MainWindow.Instance;
            var result = dialog.ShowDialog();
            if (result.HasValue && result.Value)
            {
                resizePage();
                refreshPage();
            }
        }


        private void BatchOffsetOptimize_Click()
        {
            var dialog = new BatchOffsetOptimizeDialog();
            dialog.Model.stream = this.assetFile;
            dialog.Model.StartIndex = this.Selected.Index;
            dialog.Model.EndIndex = this.assetFile.NumberOfFiles - 1;
            dialog.Owner = MainWindow.Instance;
            var result = dialog.ShowDialog();
            if (result.HasValue && result.Value)
            {
                resizePage();
                refreshPage();
            }
        }



        private void PngFormat_Click()
        {
            var dialog = new PngFormatDialog();
            dialog.Owner = MainWindow.Instance;
            dialog.ShowDialog();
        }



        private void Bmp2Png_Click()
        {
            var dialog = new BmpToPngDialog();
            dialog.Owner = MainWindow.Instance;
            dialog.ShowDialog();
        }

        private void RegFileType_Click()
        {
            if (this.IsRegFileType)
            {
                FileTypeRegister.UnRegisterFileType(".Asset");
            }
            else
            {
                FileTypeRegInfo fileTypeRegInfo = new FileTypeRegInfo(".osf");
                fileTypeRegInfo.Description = "Images Assets Resource File";
                fileTypeRegInfo.ExePath = Process.GetCurrentProcess().MainModule.FileName;
                fileTypeRegInfo.ExtendName = ".Asset";
                fileTypeRegInfo.IconPath = fileTypeRegInfo.ExePath;
                // 注册
                try
                {
                    FileTypeRegister.RegisterFileType(fileTypeRegInfo);
                }
                catch (Exception ex)
                {
                    if (ex.HResult == -2147024891)
                    {
                        MessageBox.Show("关联文件必须管理员权限启动应用。");
                    }
                    else
                    {
                        MessageBox.Show("关联失败：" + ex.Message);
                    }


                }



            }
            this.IsRegFileType = FileTypeRegister.FileTypeRegistered(".Asset");
        }

        private void ImportImage_Click()
        {
            ImportDialog import = new ImportDialog();
            import.Model.SelectedIndex = this.Selected.Index;
            import.Model.stream = this.assetFile;
            import.Owner = MainWindow.Instance;
            var result = import.ShowDialog();
            if (result.HasValue && result.Value)
            {
                resizePage();
                refreshPage();
            }
        }

        private void ChangePassword_Click()
        {
            var dialog = new PasswordInput();
            dialog.Owner = MainWindow.Instance;
            dialog.Model.Title = "修改资源包密码";
            var result = dialog.ShowDialog();
            if (result.HasValue && result.Value)
            {
                this.assetFile.ChangePassword(dialog.Model.Password);
                MessageBox.Show("密码已修改", "修改密码");
            }
        }




        private Boolean AssetFileOpened_CanClick()
        {
            return this.assetFile != null;
        }
        private void Expand_Click()
        {
            ExpandDialog expand = new ExpandDialog();
            expand.Model.stream = this.assetFile;
            expand.Owner = MainWindow.Instance;
            var result = expand.ShowDialog();
            if (result.HasValue && result.Value)
            {
                resizePage();
                refreshPage();
            }

        }

        private void Recycle_Click()
        {
            if (this.assetFile.Recycle())
            {
                var page = (UInt32)(this.assetFile.NumberOfFiles / this.PageSize);
                if (page < this.CurrentPage)
                {
                    this.CurrentPage = page;
                }
                resizePage();
                refreshPage();
            }
        }


        private Boolean CleanImage_CanClick()
        {
            return this.assetFile != null && this.Selected != null;
        }
        private void CleanImage_Click()
        {
            var result = MessageBox.Show("是否清除图片数据？", "删除确认", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                var block = new DataBlock();
                block.Index = (Int32)this.Selected.Index;
                this.assetFile.Import(new List<DataBlock>() { block });
                refreshPage();
            }
        }

        private void ExportImage_Click()
        {
            ExportDialog export = new ExportDialog();
            export.Model.stream = this.assetFile;
            if (this.SelectedImage != null)
            {
                export.Model.StartIndex = this.SelectedImage.Index;
                export.Model.EndIndex = this.SelectedImage.Index;
                export.Model.IsBatch = false;
            }
            else
            {
                export.Model.StartIndex = 0;
                export.Model.EndIndex = this.assetFile.NumberOfFiles - 1;
                export.Model.IsBatch = true;
            }
            export.Owner = MainWindow.Instance;
            export.ShowDialog();
        }


        private void OpenAssetPackage(String filename, String password)
        {
            ClosePackage_Click();
            try
            {
                this.assetFile = AssetFileStream.Open(filename, password);
                this.Title = $"Assets Editor - {filename}";
                this.CurrentPage = 0;
                resizePage();
                refreshPage();
                this.ClosePackageCommand.NotifyCanExecuteChanged();
                this.ImportImageCommand.NotifyCanExecuteChanged();
                this.CleanImageCommand.NotifyCanExecuteChanged();
                this.ExportImageCommand.NotifyCanExecuteChanged();
                this.BatchImageOptimizeCommand.NotifyCanExecuteChanged();
                this.BatchOffsetOptimizeCommand.NotifyCanExecuteChanged();
                this.ExpandCommand.NotifyCanExecuteChanged();
                this.RecycleCommand.NotifyCanExecuteChanged();
                this.ChangePasswordCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

        }

        private void ClosePackage_Click()
        {
            this.SelectedImage = null;
            this.Selected.OffsetX = 0;
            this.Selected.OffsetY = 0;
            this.Selected.Index = 0;
            this.Selected.RenderType = RenderTypes.Normal;
            this.Selected.ImageType = ImageTypes.Unknown;
            this.Selected.Source = null;
            this.CurrentPage = 0;
            if (this.assetFile != null)
            {
                this.assetFile.Close();
                this.assetFile.Dispose();
                this.assetFile = null;
                this.ClosePackageCommand.NotifyCanExecuteChanged();

                this.offsetChanged = false;
                this.OffsetCommitCommand.NotifyCanExecuteChanged();
                this.BatchOffsetCommitCommand.NotifyCanExecuteChanged();
                this.BatchImageOptimizeCommand.NotifyCanExecuteChanged();
                this.BatchOffsetOptimizeCommand.NotifyCanExecuteChanged();
                this.ImportImageCommand.NotifyCanExecuteChanged();
                this.CleanImageCommand.NotifyCanExecuteChanged();
                this.RecycleCommand.NotifyCanExecuteChanged();
                this.ExpandCommand.NotifyCanExecuteChanged();
                this.ExportImageCommand.NotifyCanExecuteChanged();
                this.ChangePasswordCommand.NotifyCanExecuteChanged();
            }
            resizePage();
            this.Title = "Assets Editor - Power by Hanks";
        }


        private void OpenPackage_Click()
        {
            OpenFileDialog ofd = new OpenFileDialog()
            {
                InitialDirectory = ConfigureUtil.GetValue("OpenAssetDirectory"),
                DereferenceLinks = false,
            };
            //ofd.InitialDirectory = @"D:\";
            ofd.Filter = "Assets Package|*.asset";
            if (ofd.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(ofd.FileName);
                ConfigureUtil.SetValue("OpenAssetDirectory", dir);
                this.OpenFile(ofd.FileName);
            }
        }


        public void OpenFile(String fileName)
        {
            var dialog = new PasswordInput();
            dialog.Owner = MainWindow.Instance;
            var result = dialog.ShowDialog();
            if (result.HasValue && result.Value)
            {
                this.OpenAssetPackage(fileName, dialog.Model.Password);
            }
        }






        private void resizePage()
        {
            UInt32 numberOfFiles = 0;
            if (this.assetFile == null) return;
            if (this.assetFile != null) numberOfFiles = this.assetFile.NumberOfFiles;
            var pagesize2 = numberOfFiles - (this.CurrentPage * this.PageSize);
            var pagesize = (UInt32)Math.Min(this.PageSize, pagesize2);
            this.PageElementCount = pagesize;
            this.GridImages.Clear();
            for (int i = 0; i < pagesize; i++)
            {
                this.GridImages.Add(new ImageModel());
            }
            this.TotalPage = (UInt32)Math.Ceiling((Double)numberOfFiles / this.PageSize) - 1;
            if (this.CurrentPage > this.totalPage)
            {
                this.CurrentPage = this.totalPage;
            }
        }

        private void refreshPage()
        {
            if (this.assetFile == null) return;
            var pagesize2 = this.assetFile.NumberOfFiles - (this.CurrentPage * this.PageSize);
            var pagesize = Math.Min(this.PageSize, pagesize2);
            if (this.PageElementCount != pagesize)
            {
                this.resizePage();
            }
            var startAt = this.CurrentPage * this.PageSize;
            for (int i = 0; i < pagesize; i++)
            {
                if (startAt + i < this.assetFile.NumberOfFiles)
                {
                    var node = this.assetFile.Read((UInt32)(startAt + i));
                    this.GridImages[i].Index = (UInt32)(startAt + i);
                    this.GridImages[i].ImageType = node.lpType;
                    this.GridImages[i].RenderType = node.lpRenderType;
                    this.GridImages[i].OffsetX = node.OffsetX;
                    this.GridImages[i].OffsetY = node.OffsetY;
                    this.GridImages[i].Width = node.Width;
                    this.GridImages[i].Height = node.Height;

                    this.GridImages[i].FileSize = node.Data.Length;
                    var source = loadImageSource(node);
                    this.GridImages[i].Source = source;
                }
            }
        }

        public BitmapSource LoadBitmap(byte[] pixelData, int width, int height)
        {
            System.Windows.Media.PixelFormat format = PixelFormats.Bgra32; // 32-bit BGR format
            int stride = width * format.BitsPerPixel / 8;
            BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, format, null, pixelData, stride);
            return bitmap;
        }

        private BitmapSource loadImageSource(IReadOnlyDataBlock node)
        {
            try
            {
                if (node.Data.Length == 0) return BitmapUtil.EmptyBitmapSource;
                if (node.lpType == ImageTypes.PNG)
                {
                    //AlphaUtil.UnpremultiplyAlpha(node.Data);
                }
                AlphaUtil.SwitchRedBlue(node.Data);
                return LoadBitmap(node.Data, node.Width, node.Height);
            }
            catch (Exception ex)
            {
                return null;
            }
        }



        private void Themes_Click()
        {
            // /Assets.Editor;component/Assets/Themes/background.png
            if (ThemeManager.CurrentTheme == "White")
            {
                ThemeManager.LoadThemeFromResource("/Assets.Editor;component/Assets/Themes/Black.xaml");
            }
            else if (ThemeManager.CurrentTheme == "Black")
            {
                ThemeManager.LoadThemeFromResource("/Assets.Editor;component/Assets/Themes/White.xaml");
            }
            //Console.WriteLine(ThemeManager.CurrentTheme);
        }





        private void ScrollBar_ValueChanged(RoutedPropertyChangedEventArgs<double> e)
        {
            refreshPage();
            if (this.SelectedImage != null)
            {
                this.Selected.CopyFrom(this.SelectedImage);
            }
        }

        private void ListView_SelectionChanged(System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                this.SelectedImage = e.AddedItems[0] as ImageModel;
                this.Selected.CopyFrom(this.SelectedImage);
            }
            else
            {
                this.SelectedImage = null;
            }
            this.ExportImageCommand.NotifyCanExecuteChanged();
            this.CleanImageCommand.NotifyCanExecuteChanged();
        }


        private void ListGrid_SelectionChanged(SelectionChangedEventArgs e)
        {
            //if (this.ListGrid == ViewGrids.GRID_8X4)
            //{
            //    this.GridColumns = 8;
            //    this.GridRows = 4;
            //}
            //else if (this.ListGrid == ViewGrids.GRID_10X4)
            //{
            //    this.GridColumns = 10;
            //    this.GridRows = 4;
            //}
            //else if (this.ListGrid == ViewGrids.GRID_16X4)
            //{
            //    this.GridColumns = 16;
            //    this.GridRows = 4;
            //}
        }



        private void RenderType_SelectionChanged(System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (this.SelectedImage != null && (this.Selected.RenderType != this.SelectedImage.RenderType))
            {
                this.offsetChanged = true;
                this.OffsetCommitCommand.NotifyCanExecuteChanged();
                this.BatchOffsetCommitCommand.NotifyCanExecuteChanged();
            }
        }

        private void Offset_TextChanged(System.Windows.Controls.TextChangedEventArgs e)
        {
            if (this.SelectedImage != null && (this.Selected.OffsetX != this.SelectedImage.OffsetX || this.Selected.OffsetY != this.SelectedImage.OffsetY))
            {
                this.offsetChanged = true;
                this.OffsetCommitCommand.NotifyCanExecuteChanged();
                this.BatchOffsetCommitCommand.NotifyCanExecuteChanged();
            }
        }

        private Boolean offsetChanged = false;


        private void BatchOffsetCommit_Click()
        {
            var dialog = new BatchOffsetDialog();
            dialog.Owner = MainWindow.Instance;
            dialog.Model.stream = this.assetFile;
            dialog.Model.OffsetX = this.selected.OffsetX;
            dialog.Model.OffsetY = this.selected.OffsetY;
            dialog.Model.EndIndex = this.Selected.Index;
            dialog.Model.StartIndex = this.Selected.Index;

            var result = dialog.ShowDialog();
            if (result.HasValue && result.Value)
            {
                this.refreshPage();
                this.offsetChanged = false;
                this.OffsetCommitCommand.NotifyCanExecuteChanged();
                this.BatchOffsetCommitCommand.NotifyCanExecuteChanged();
            }
        }

        private Boolean OffsetCommit_CanClick()
        {
            return this.offsetChanged;
        }

        private void OffsetCommit_Click()
        {
            this.SelectedImage.OffsetX = this.Selected.OffsetX;
            this.SelectedImage.OffsetY = this.Selected.OffsetY;
            this.SelectedImage.RenderType = this.Selected.RenderType;
            this.assetFile.UpdateInfo(this.SelectedImage.Index, new DataInfo()
            {
                lpRenderType = this.Selected.RenderType,
                OffsetX = this.Selected.OffsetX,
                OffsetY = this.Selected.OffsetY,
            });

            // this.assetFile.UpdateOffsetNoWrite(this.SelectedImage.Index, new System.Drawing.Point(this.Selected.OffsetX, this.Selected.OffsetY));
            this.offsetChanged = false;
            this.OffsetCommitCommand.NotifyCanExecuteChanged();
            this.BatchOffsetCommitCommand.NotifyCanExecuteChanged();
        }




        /// <summary>
        /// 当前页实际显示的元素数量
        /// </summary>
        public Boolean IsRegFileType
        {
            get
            {
                return this.isRegFileType;
            }
            set
            {
                base.SetProperty(ref this.isRegFileType, value);
            }
        }
        private Boolean isRegFileType;



        private void ListView_PreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (e.Delta > 0 && this.CurrentPage > 0)
            {
                this.CurrentPage--;
            }
            else if (e.Delta < 0 && this.CurrentPage < this.TotalPage)
            {
                this.CurrentPage++;
            }
        }



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


        public Double ZoomValue
        {
            get
            {
                return this.zoomValue;
            }
            set
            {
                base.SetProperty(ref this.zoomValue, value);
            }
        }
        private Double zoomValue;


        public ImageModel SelectedImage
        {
            get
            {
                return this.selectedImage;
            }
            set
            {
                base.SetProperty(ref this.selectedImage, value);
            }
        }
        private ImageModel selectedImage;


        public UInt32 PageElementCount
        {
            get
            {
                return this.pageEleCount;
            }
            set
            {
                base.SetProperty(ref this.pageEleCount, value);
            }
        }
        private UInt32 pageEleCount;



        public Int32 PageSize
        {
            get
            {
                return this.pageSize;
            }
            set
            {
                base.SetProperty(ref this.pageSize, value);
            }
        }
        private Int32 pageSize;



        public UInt32 TotalPage
        {
            get
            {
                return this.totalPage;
            }
            set
            {
                base.SetProperty(ref this.totalPage, value);
            }


        }
        private UInt32 totalPage;

        public UInt32 CurrentPage
        {
            get
            {
                return this.currentPage;
            }
            set
            {
                base.SetProperty(ref this.currentPage, value);
            }
        }

        private UInt32 currentPage;







        public ImageModel Selected
        {
            get
            {
                return this.selected;
            }
            set
            {
                base.SetProperty(ref this.selected, value);
            }
        }

        private ImageModel selected;



        public ViewGrids ListGrid
        {
            get
            {
                return this.listGrid;
            }
            set
            {
                base.SetProperty(ref this.listGrid, value);

                if (this.ListGrid == ViewGrids.GRID_8X4)
                {
                    this.GridColumns = 8;
                    this.GridRows = 4;
                }
                else if (this.ListGrid == ViewGrids.GRID_10X4)
                {
                    this.GridColumns = 10;
                    this.GridRows = 4;
                }
                else if (this.ListGrid == ViewGrids.GRID_12X4)
                {
                    this.GridColumns = 12;
                    this.GridRows = 4;
                }
                else if (this.ListGrid == ViewGrids.GRID_16X4)
                {
                    this.GridColumns = 16;
                    this.GridRows = 4;
                }
                else if (this.ListGrid == ViewGrids.GRID_20X4)
                {
                    this.GridColumns = 20;
                    this.GridRows = 4;
                }
                var firstIndex = this.PageSize * this.CurrentPage;
                this.PageSize = this.GridColumns * this.GridRows;

                this.CurrentPage = (UInt32)(firstIndex / this.PageSize);


                refreshPage();
            }
        }

        private ViewGrids listGrid;






        public Int32 GridColumns
        {
            get
            {
                return this.gridColumns;
            }
            set
            {
                base.SetProperty(ref this.gridColumns, value);
            }
        }

        private Int32 gridColumns;


        public Int32 GridRows
        {
            get
            {
                return this.gridRows;
            }
            set
            {
                base.SetProperty(ref this.gridRows, value);
            }
        }

        private Int32 gridRows;
















        public Boolean ZeroLine
        {
            get
            {
                return this.zeroLine;
            }
            set
            {
                base.SetProperty(ref this.zeroLine, value);
            }
        }

        private Boolean zeroLine;

        public Boolean HumLine
        {
            get
            {
                return this.humLine;
            }
            set
            {
                base.SetProperty(ref this.humLine, value);
            }
        }

        private Boolean humLine;

        public Boolean BoundsLine
        {
            get
            {
                return this.boundsLine;
            }
            set
            {
                base.SetProperty(ref this.boundsLine, value);
            }
        }

        private Boolean boundsLine;

        public Rect CustomRect
        {
            get
            {
                return this.customRect;
            }
            set
            {
                base.SetProperty(ref this.customRect, value);
            }
        }

        private Rect customRect;








    }
}
