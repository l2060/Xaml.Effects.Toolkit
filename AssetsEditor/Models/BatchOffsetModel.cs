using Microsoft.Toolkit.Mvvm.Input;
using Resource.Package.Assets;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Input;
using Xaml.Effects.Toolkit.Model;


namespace Assets.Editor.Models
{


    public enum OffsetBatchMode
    {

        [Description("绝对偏移")]
        Absolute = 1,

        [Description("相对偏移")]
        Relative = 2

    }






    public class BatchOffsetModel : DialogModel
    {
        public readonly String ImageFilter = "Image File|*.png;*.bmp;*.gif;*.jpg;*.tif;*.tga";
        public ICommand SelectSourceCommand { get; protected set; }
        public IRelayCommand ModeChangedCommand { get; protected set; }

        public AssetFileStream stream { get; set; }



        public BatchOffsetModel()
        {
            this.Title = "批量修改偏移";
            this.StartIndex = 0;
            this.EndIndex = 0;
            this.BatchMode = OffsetBatchMode.Relative;
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
            var offsets = new Dictionary<UInt32, Point>();
            Point? relativeOffset = null;

            if (this.BatchMode == OffsetBatchMode.Relative)
            {
                var info = this.stream.GetInfomation(this.StartIndex);
                var offx = this.OffsetX - info.OffsetX;
                var offy = this.OffsetY - info.OffsetY;
                relativeOffset = new Point(offx, offy);
            }

            for (UInt32 i = this.StartIndex; i <= this.endIndex; i++)
            {
                var point = new Point(this.OffsetX, this.OffsetY);
                if (this.BatchMode == OffsetBatchMode.Relative)
                {
                    var info = this.stream.GetInfomation(i);
                    var offx = info.OffsetX + relativeOffset.Value.X;
                    var offy = info.OffsetY + relativeOffset.Value.Y;
                    point = new Point(offx, offy);
                }
                offsets[i] = point;
            }
            this.stream.UpdateOffsets(offsets);
            this.DialogResult = true;

        }

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



        public OffsetBatchMode BatchMode
        {
            get
            {
                return this.batchMode;
            }
            set
            {
                base.SetProperty(ref this.batchMode, value);
            }
        }
        private OffsetBatchMode batchMode;
    }
}
