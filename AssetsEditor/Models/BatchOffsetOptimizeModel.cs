using Microsoft.Toolkit.Mvvm.Input;
using Resource.Package.Assets;
using Resource.Package.Assets.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Input;
using Xaml.Effects.Toolkit.Model;


namespace Assets.Editor.Models
{


    public class BatchOffsetOptimizeModel : DialogModel
    {
        public ICommand SelectSourceCommand { get; protected set; }
        public IRelayCommand ModeChangedCommand { get; protected set; }

        public AssetFileStream stream { get; set; }



        public BatchOffsetOptimizeModel()
        {
            this.Title = "批量剪裁优化工具";
            this.StartIndex = 0;
            this.EndIndex = 0;
            this.MinOffsetX = Int32.MaxValue;
            this.MinOffsetY = Int32.MaxValue;
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
            Dictionary<UInt32, IReadOnlyDataBlock> list = new Dictionary<UInt32, IReadOnlyDataBlock>();
            for (UInt32 i = this.StartIndex; i <= this.endIndex; i++)
            {
                var node = this.stream.Read(i);
                if (node.OffsetX < this.MinOffsetX) this.MinOffsetX = node.OffsetX;
                if (node.OffsetY < this.MinOffsetY) this.MinOffsetY = node.OffsetY;
                list.Add(i, node);
            }

            var blocks = new List<DataBlock>();
            foreach (var item in list)
            {
                var block = this.OptimizeImageOffset(item.Key, item.Value);
                blocks.Add(block);
            }
            if (list.Count > 0)
            {
                this.stream.Import(blocks);
            }
            this.DialogResult = true;
        }

        private DataBlock OptimizeImageOffset(UInt32 index, IReadOnlyDataBlock node)
        {
            var block = new DataBlock();
            block.Index = (Int32)index;
            block.lpRenderType = node.lpRenderType;
            block.lpType = node.lpType;
            block.Unknown1 = node.Unknown1;
            block.Unknown2 = node.Unknown2;

            // 计算偏移量
            var leftPadding = node.OffsetX - this.MinOffsetX;
            var topPadding = node.OffsetY - this.MinOffsetY;

            // 新的宽高不变
            block.Width = node.Width;
            block.Height = node.Height;

            // 新的偏移归0
            block.OffsetX = 0;
            block.OffsetY = 0;

            // 原始数据
            var srcData = node.Data;
            int srcWidth = node.Width;
            int srcHeight = node.Height;
            int bytesPerPixel = 4; // 假设为32位图像
            int srcStride = srcWidth * bytesPerPixel;

            // 目标数据
            var dstData = new byte[srcWidth * srcHeight * bytesPerPixel];
            int dstStride = srcWidth * bytesPerPixel;

            // 填充透明像素
            for (int y = 0; y < srcHeight; y++)
            {
                for (int x = 0; x < srcWidth; x++)
                {
                    int dstIndex = (y * dstStride) + (x * bytesPerPixel);
                    // 判断是否在原图有效区域内
                    int srcX = x - leftPadding;
                    int srcY = y - topPadding;
                    if (srcX >= 0 && srcX < srcWidth && srcY >= 0 && srcY < srcHeight)
                    {
                        int srcIndex = (srcY * srcStride) + (srcX * bytesPerPixel);
                        Buffer.BlockCopy(srcData, srcIndex, dstData, dstIndex, bytesPerPixel);
                    }
                    else
                    {
                        // 填充透明像素
                        dstData[dstIndex + 0] = 0;
                        dstData[dstIndex + 1] = 0;
                        dstData[dstIndex + 2] = 0;
                        dstData[dstIndex + 3] = 0;
                    }
                }
            }

            block.Data = dstData;
            return block;
        }


        public Int32 MinOffsetX { get; set; }
        public Int32 MinOffsetY { get; set; }



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


    }
}
