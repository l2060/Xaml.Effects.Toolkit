using System.ComponentModel;

namespace Resource.Package.Assets.Common
{
    public enum CompressionOption : Byte
    {
        /// <summary>
        /// 永远不会压缩
        /// </summary>
        [Description("不要压缩")]
        NeverCompress = 1,

        /// <summary>
        /// 尽可能压缩
        /// </summary>
        [Description("尽可能压缩")]
        MuchPossibleCompress = 2,

        /// <summary>
        /// 必须压缩
        /// </summary>
        [Description("必须压缩")]
        MustCompressed = 3
    }







    internal struct FileHeader
    {
        public UInt64 Magic { get; set; }

        public CompressionOption CompressOption { get; set; }


        public Byte[] Version { get; set; }


        /// <summary>
        /// 目录数量
        /// </summary>
        public UInt32 NumberOfFiles { get; set; }
        public UInt32 TableDataAddr { get; set; }
        public UInt32 TableDataSize { get; set; }

    }



    internal class FileAsyncCache
    {
        public Byte[] Data;
    }



    public enum ImageTypes : Byte
    {
        [Description("Unknown")]
        Unknown = 0,
        [Description("Bmp")]
        BMP = 1,
        [Description("Png")]
        PNG = 2,
        [Description("Jpg")]
        JPG = 3,
        [Description("Gif")]
        GIF = 4,
        [Description("Tga")]
        TGA = 5,
        [Description("Tiff")]
        TIFF = 6
    }

    public enum RenderTypes : Byte
    {
        [Description("正常渲染")]
        Normal = 0,
        [Description("去除底色")]
        MaskColor = 1,
        [Description("混合渲染")]
        Blend = 2
    }




    public enum ImportOption
    {
        [Description("追加")]
        Append = 0,
        [Description("覆盖")]
        Override = 1,

    }


    unsafe public delegate Byte[]? DataReader(Int32 index);


    public interface IReadOnlyLazyInfo
    {
        public Int32 Index { get; }
        public Int32 Width { get; }
        public Int32 Height { get; }
        public Int16 OffsetX { get; }
        public Int16 OffsetY { get; }
        public ImageTypes lpType { get; }
        public RenderTypes lpRenderType { get; }
        public Byte Unknown2 { get; }
        public Byte Unknown1 { get; }

        public DataReader ReadData { get; }
    }




    public class LazyDataInfo : IReadOnlyLazyInfo
    {
        public Int32 Index { get; set; }
        public Int32 Width { get; set; }
        public Int32 Height { get; set; }


        public Int16 OffsetX { get; set; }
        public Int16 OffsetY { get; set; }

        public ImageTypes lpType { get; set; }
        public RenderTypes lpRenderType { get; set; }
        public Byte Unknown2 { get; set; }
        public Byte Unknown1 { get; set; }

        public DataReader ReadData { get; set; }

    }



    public interface IReadOnlyDataInfo
    {
        public Int32 Width { get; }
        public Int32 Height { get; }
        public Int16 OffsetX { get; }
        public Int16 OffsetY { get; }
        public ImageTypes lpType { get; }
        public RenderTypes lpRenderType { get; }
        public Byte Unknown2 { get; }
        public Byte Unknown1 { get; }
    }



    public interface IReadOnlyDataBlock : IReadOnlyDataInfo
    {
        public Byte[] Data { get; }
    }


    public class DataInfo
    {

        public Int32 Width { get; set; }
        public Int32 Height { get; set; }


        public Int16 OffsetX { get; set; }
        public Int16 OffsetY { get; set; }

        public RenderTypes lpRenderType { get; set; }
        public Byte Unknown2 { get; set; }
        public Byte Unknown1 { get; set; }




    }










    public class DataBlock : DataInfo, IReadOnlyDataBlock
    {
        public DataBlock()
        {
            this.Data = new byte[0];
        }
        /// <summary>
        /// 小于0 追加 大于0 覆盖替换
        /// </summary>
        public Int32 Index { get; set; }
        public ImageTypes lpType { get; set; }
        public Byte[] Data { get; set; }
    }


    internal class FileInfomation : DataInfo
    {



        public ImageTypes lpType { get; set; }
        /// <summary>
        /// 数据地址
        /// 4字节
        /// </summary>
        public UInt32 lpData { get; set; }

        /// <summary>
        /// 原始数据大小
        /// </summary>
        public UInt32 lpRawSize { get; set; }

        /// <summary>
        /// 数据大小
        /// 4字节
        /// </summary>
        public UInt32 lpSize { get; set; }






        public void CopyFrom(FileInfomation info)
        {
            this.OffsetY = info.OffsetY;
            this.OffsetX = info.OffsetX;
            this.Unknown1 = info.Unknown1;
            this.Unknown2 = info.Unknown2;
            this.lpData = info.lpData;
            this.lpType = info.lpType;
            this.lpRenderType = info.lpRenderType;
            this.lpRawSize = info.lpRawSize;
            this.lpSize = info.lpSize;

        }

    }




}
