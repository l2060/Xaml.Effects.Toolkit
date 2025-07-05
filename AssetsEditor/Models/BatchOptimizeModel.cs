using Microsoft.Toolkit.Mvvm.Input;
using Resource.Package.Assets;
using Resource.Package.Assets.Common;
using System;
using System.Collections.Generic;
using System.Windows.Input;
using Xaml.Effects.Toolkit.Model;


namespace Assets.Editor.Models
{

    public struct CutRectangle
    {
        public CutRectangle(Int32 left, Int32 top, Int32 right, Int32 bottom)
        {
            this.Top = top;
            this.Left = left;
            this.Right = right;
            this.Bottom = bottom;
        }



        public Int32 Left;
        public Int32 Right;

        public Int32 Top;
        public Int32 Bottom;



    }



    public class BatchOptimizeModel : DialogModel
    {
        public ICommand SelectSourceCommand { get; protected set; }
        public IRelayCommand ModeChangedCommand { get; protected set; }

        public AssetFileStream stream { get; set; }



        public BatchOptimizeModel()
        {
            this.Title = "批量剪裁优化工具";
            this.StartIndex = 0;
            this.EndIndex = 0;
            this.CutLeft = true;
            this.CutRight = true;
            this.CutTop = true;
            this.CutBottom = true;
            this.ContainerWidth = 38;
            this.ContainerHeight = 38;
        }


        protected override bool Can_Submit()
        {
            return true;
        }

        /// <summary>
        /// 提交按钮事件
        /// </summary>
        protected override void Execute_Submit()
        {
            List<DataBlock> list = new List<DataBlock>();
            for (UInt32 i = this.StartIndex; i <= this.endIndex; i++)
            {
                var node = this.stream.Read(i);
                var outnode = this.OptimizeImage(i, node);
                if (outnode != null)
                {
                    list.Add(outnode);
                }
            }
            if (list.Count > 0)
            {
                this.stream.Import(list);
            }

            this.DialogResult = true;
        }

        private DataBlock OptimizeImage(UInt32 index, IReadOnlyDataBlock node)
        {
            var block = new DataBlock();
            block.Index = (Int32)index;
            block.lpRenderType = node.lpRenderType;
            block.lpType = node.lpType;
            block.Unknown1 = node.Unknown1;
            block.Unknown2 = node.Unknown2;
            block.OffsetX = node.OffsetX;
            block.OffsetY = node.OffsetY;
            CutRectangle rectangle = new CutRectangle(0, 0, node.Width, node.Height);
            if (this.CutLeft) this.FindBoundLeft(node, ref rectangle);
            if (this.CutTop) this.FindBoundTop(node, ref rectangle);
            if (this.CutRight) this.FindBoundRight(node, ref rectangle);
            if (this.CutBottom) this.FindBoundBottom(node, ref rectangle);
            var width = rectangle.Right - rectangle.Left;
            var height = rectangle.Bottom - rectangle.Top;
            block.Width = width;
            block.Height = height;
            block.Data = node.Data;
            if (rectangle.Left != 0 || rectangle.Top != 0 || rectangle.Right != node.Width || rectangle.Bottom != node.Height)
            {
                var size = width * height * 4;
                block.Data = new byte[size];
                var srcStride = node.Width * 4;
                var dstStride = width * 4;
                var rowOffset = rectangle.Left * 4;
                for (int i = 0; i < height; i++)
                {
                    Buffer.BlockCopy(node.Data, (rectangle.Top + i) * srcStride + rowOffset, block.Data, i * dstStride, dstStride);
                }
            }
            if (this.CenterAlign)
            {
                var offsetX = (this.containerWidth - width) / 2;
                var offsetY = (this.containerHeight - height) / 2;
                block.OffsetX = (Int16)offsetX;
                block.OffsetY = (Int16)offsetY;
            }

            if (block.OffsetX == node.OffsetX && block.OffsetY == node.OffsetY && block.Data.Length == node.Data.Length)
            {
                return null;
            }

            return block;
        }




        #region Find Bounds Function

        unsafe private void FindBoundLeft(IReadOnlyDataBlock node, ref CutRectangle rectangle)
        {
            var Stride = node.Width * 4;
            // 指向位图数据的指针
            fixed (byte* ptr = node.Data)
            {
                // 从左侧找到第一个非白像素的位置
                for (int x = 0; x < rectangle.Right; x++)
                {
                    for (int y = 0; y < rectangle.Bottom; y++)
                    {
                        byte alpha = *(ptr + y * Stride + x * 4 + 3);
                        if (alpha != 0)
                        {
                            rectangle.Left = x;
                            return;
                        }
                    }
                }
            }
            rectangle.Left = rectangle.Right;
        }
        unsafe private void FindBoundRight(IReadOnlyDataBlock node, ref CutRectangle rectangle)
        {
            var Stride = node.Width * 4;
            // 指向位图数据的指针
            fixed (byte* ptr = node.Data)
            {
                // 从右侧找到第一个非白像素的位置
                for (int x = rectangle.Right - 1; x >= rectangle.Left; x--)
                {
                    for (int y = 0; y < rectangle.Bottom; y++)
                    {
                        byte alpha = *(ptr + y * Stride + x * 4 + 3);
                        if (alpha != 0)
                        {
                            rectangle.Right = x + 1;
                            return;
                        }
                    }
                }
            }

            rectangle.Right = rectangle.Left;
        }
        unsafe private void FindBoundTop(IReadOnlyDataBlock node, ref CutRectangle rectangle)
        {
            var Stride = node.Width * 4;
            // 指向位图数据的指针
            fixed (byte* ptr = node.Data)
            {
                // 从顶部找到第一个非白像素的位置
                for (int y = rectangle.Top; y < rectangle.Bottom; y++)
                {
                    for (int x = rectangle.Left; x < rectangle.Right; x++)
                    {
                        byte alpha = *(ptr + y * Stride + x * 4 + 3);
                        if (alpha != 0)
                        {
                            rectangle.Top = y;
                            return;
                        }
                    }
                }
            }
            rectangle.Top = rectangle.Bottom;
        }
        unsafe private void FindBoundBottom(IReadOnlyDataBlock node, ref CutRectangle rectangle)
        {
            var Stride = node.Width * 4;
            // 指向位图数据的指针
            fixed (byte* ptr = node.Data)
            {
                // 从底部找到第一个非白像素的位置
                for (int y = rectangle.Bottom - 1; y >= rectangle.Top; y--)
                {
                    for (int x = rectangle.Left; x < rectangle.Right; x++)
                    {
                        byte alpha = *(ptr + y * Stride + x * 4 + 3);
                        if (alpha != 0)
                        {
                            rectangle.Bottom = y + 1;
                            return;
                        }
                    }
                }
            }
            rectangle.Bottom = rectangle.Top;
        }


        #endregion

        public Int32 OffsetX { get; set; }
        public Int32 OffsetY { get; set; }



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







        public Boolean CutLeft

        {
            get
            {
                return this.cutLeft;
            }
            set
            {
                base.SetProperty(ref this.cutLeft, value);
            }
        }
        private Boolean cutLeft;


        public Boolean CutTop

        {
            get
            {
                return this.cutTop;
            }
            set
            {
                base.SetProperty(ref this.cutTop, value);
            }
        }
        private Boolean cutTop;


        public Boolean CutRight

        {
            get
            {
                return this.cutRight;
            }
            set
            {
                base.SetProperty(ref this.cutRight, value);
            }
        }
        private Boolean cutRight;

        public Boolean CutBottom

        {
            get
            {
                return this.cutBottom;
            }
            set
            {
                base.SetProperty(ref this.cutBottom, value);
            }
        }
        private Boolean cutBottom;







        public Boolean CenterAlign

        {
            get
            {
                return this.centerAlign;
            }
            set
            {
                base.SetProperty(ref this.centerAlign, value);
            }
        }
        private Boolean centerAlign;



        public Int32 ContainerWidth

        {
            get
            {
                return this.containerWidth;
            }
            set
            {
                base.SetProperty(ref this.containerWidth, value);
            }
        }
        private Int32 containerWidth;



        public Int32 ContainerHeight

        {
            get
            {
                return this.containerHeight;
            }
            set
            {
                base.SetProperty(ref this.containerHeight, value);
            }
        }
        private Int32 containerHeight;



    }
}
