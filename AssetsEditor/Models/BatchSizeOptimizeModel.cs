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


    public class BatchSizeOptimizeModel : DialogModel
    {
        public ICommand SelectSourceCommand { get; protected set; }
        public IRelayCommand ModeChangedCommand { get; protected set; }

        public AssetFileStream stream { get; set; }



        public BatchSizeOptimizeModel()
        {
            this.Title = "批量尺寸调整工具";
            this.StartIndex = 0;
            this.EndIndex = 0;
            this.TargetWidth = 32;
            this.TargetHeight = 32;
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
            var blocks = new List<DataBlock>();
            for (UInt32 i = this.StartIndex; i <= this.endIndex; i++)
            {
                var node = this.stream.Read(i);
                var block = this.OptimizeImageOffset(i, node);
                blocks.Add(block);
            }
            if (blocks.Count > 0)
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

            // 新的宽高为目标宽高
            int targetWidth = (int)this.TargetWidth;
            int targetHeight = (int)this.TargetHeight;
            block.Width = targetWidth;
            block.Height = targetHeight;

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
            var dstData = new byte[targetWidth * targetHeight * bytesPerPixel];
            int dstStride = targetWidth * bytesPerPixel;

            // 最近邻或双线性插值缩放（优先双线性）
            for (int y = 0; y < targetHeight; y++)
            {
                float srcY = ((float)y / (float)targetHeight) * srcHeight;
                int y0 = (int)Math.Floor(srcY);
                int y1 = Math.Min(y0 + 1, srcHeight - 1);
                float fy = srcY - y0;

                for (int x = 0; x < targetWidth; x++)
                {
                    float srcX = ((float)x / (float)targetWidth) * srcWidth;
                    int x0 = (int)Math.Floor(srcX);
                    int x1 = Math.Min(x0 + 1, srcWidth - 1);
                    float fx = srcX - x0;

                    // 双线性插值
                    int dstIndex = (y * dstStride) + (x * bytesPerPixel);
                    for (int c = 0; c < bytesPerPixel; c++)
                    {
                        int i00 = (y0 * srcStride) + (x0 * bytesPerPixel) + c;
                        int i01 = (y0 * srcStride) + (x1 * bytesPerPixel) + c;
                        int i10 = (y1 * srcStride) + (x0 * bytesPerPixel) + c;
                        int i11 = (y1 * srcStride) + (x1 * bytesPerPixel) + c;
                        float v00 = srcData[i00];
                        float v01 = srcData[i01];
                        float v10 = srcData[i10];
                        float v11 = srcData[i11];
                        float v0 = v00 + (v01 - v00) * fx;
                        float v1 = v10 + (v11 - v10) * fx;
                        float v = v0 + (v1 - v0) * fy;
                        dstData[dstIndex + c] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
                    }
                }
            }

            block.Data = dstData;
            return block;
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








        public UInt32 TargetWidth
        {
            get
            {
                return this.targetWidth;
            }
            set
            {
                base.SetProperty(ref this.targetWidth, value);
            }
        }
        private UInt32 targetWidth;


        public UInt32 TargetHeight

        {
            get
            {
                return this.targetHeight;
            }
            set
            {
                base.SetProperty(ref this.targetHeight, value);
            }
        }
        private UInt32 targetHeight;


        

    }
}
