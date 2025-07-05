using Microsoft.Toolkit.Mvvm.ComponentModel;
using Resource.Package.Assets.Common;
using System;
using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace Assets.Editor.Models
{


    public enum ImportOption
    {
        [Description("追加")]
        Append = 0,
        [Description("覆盖")]
        Override = 1,
        [Description("替换")]
        Replace = 2,

    }




    public enum ImageUserData
    {

        [Description("None")]
        None = 0,
        [Description("Placements")]
        Placements = 1,
        [Description("Schema-Json")]
        SchemaJson = 2
    }


    public enum ImageFliterOption
    {
        [Description("所有图片")]
        AllImage = 0,





    }

    [Flags]
    public enum ImageFormat
    {
        [Description("Bmp 文件")]
        BMP = 2,
        [Description("Png 文件")]
        PNG = 4,
        [Description("Jpg 文件")]
        JPG = 8,
        [Description("Gif 文件")]
        GIF = 16,
        [Description("Tga 文件")]
        TGA = 32,
        [Description("Tiff 文件")]
        TIFF = 64,
        [Description("所有支持的图片")]
        ALLIMAGE = BMP & PNG & JPG & TGA & GIF & TIFF

    }



    public class ImageModel : ObservableObject
    {
        public ImageModel()
        {


        }

        public void CopyFrom(ImageModel mode)
        {
            this.Source = mode.Source;
            this.OffsetX = mode.OffsetX;
            this.OffsetY = mode.OffsetY;
            this.ImageType = mode.ImageType;
            this.RenderType = mode.RenderType;
            this.Index = mode.Index;
            this.FileSize = mode.FileSize;
            this.Width = mode.Width;
            this.Height = mode.Height;
        }






        public BitmapSource Source
        {
            get
            {
                return this.image;
            }
            set
            {
                base.SetProperty(ref this.image, value);
            }
        }

        private BitmapSource image;



        public ImageTypes ImageType
        {
            get
            {
                return this.type;
            }
            set
            {
                base.SetProperty(ref this.type, value);
            }
        }

        private ImageTypes type;



        public RenderTypes RenderType
        {
            get
            {
                return this.render;
            }
            set
            {
                base.SetProperty(ref this.render, value);
            }
        }

        private RenderTypes render;



        public UInt32 Index
        {
            get
            {
                return this.index;
            }
            set
            {
                base.SetProperty(ref this.index, value);
            }
        }

        private UInt32 index;


        public Int16 OffsetX
        {
            get
            {
                return this.offsetX;
            }
            set
            {
                base.SetProperty(ref this.offsetX, value);
            }
        }

        private Int16 offsetX;


        public Int32 Width
        {
            get
            {
                return this.width;
            }
            set
            {
                base.SetProperty(ref this.width, value);
                base.OnPropertyChanging("ImageSize");
            }
        }

        private Int32 width;



        public Int32 Height
        {
            get
            {
                return this.height;
            }
            set
            {
                base.SetProperty(ref this.height, value);
                this.ImageSize = this.width + "x" + this.height;
            }
        }

        private Int32 height;






        public String ImageSize
        {
            get
            {
                return this.imageSize;
            }
            set
            {
                base.SetProperty(ref this.imageSize, value);

            }
        }


        public String imageSize;



        public Int16 OffsetY
        {
            get
            {
                return this.offsetY;
            }
            set
            {
                base.SetProperty(ref this.offsetY, value);
            }
        }

        private Int16 offsetY;


        public Int32 FileSize
        {
            get
            {
                return this.fileSize;
            }
            set
            {
                base.SetProperty(ref this.fileSize, value);
            }
        }

        private Int32 fileSize;

    }



}
