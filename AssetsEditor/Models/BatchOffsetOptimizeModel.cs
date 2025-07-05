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

            var leftPadding = node.OffsetX - this.MinOffsetX;
            var topPadding = node.OffsetY - this.MinOffsetY;





            //CutRectangle rectangle = new CutRectangle(0, 0, node.Width, node.Height);
            //var width = rectangle.Right - rectangle.Left;
            //var height = rectangle.Bottom - rectangle.Top;
            //block.Width = width;
            //block.Height = height;
            //block.Data = node.Data;
            //if (rectangle.Left != 0 || rectangle.Top != 0 || rectangle.Right != node.Width || rectangle.Bottom != node.Height)
            //{
            //    var size = width * height * 4;
            //    block.Data = new byte[size];
            //    var srcStride = node.Width * 4;
            //    var dstStride = width * 4;
            //    var rowOffset = rectangle.Left * 4;
            //    for (int i = 0; i < height; i++)
            //    {
            //        Buffer.BlockCopy(node.Data, (rectangle.Top + i) * srcStride + rowOffset, block.Data, i * dstStride, dstStride);
            //    }
            //}




            block.OffsetX = 0;
            block.OffsetY = 0;
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
