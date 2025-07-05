using Microsoft.Toolkit.Mvvm.Input;
using Resource.Package.Assets;
using Resource.Package.Assets.Common;
using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using Xaml.Effects.Toolkit.Model;
namespace Assets.Editor.Models
{

    public class ExpandDialogModel : DialogModel
    {
        private AssetFileStream _stream;
        public AssetFileStream stream
        {
            get
            {
                return _stream;
            }
            set
            {
                _stream = value;
                this.Capacity = _stream.NumberOfFiles + this.added;
            }
        }


        public ICommand InputChangedCommand { get; protected set; }
        public ExpandDialogModel()
        {
            this.Title = "资源包扩容";
            this.InputChangedCommand = new RelayCommand<TextChangedEventArgs>(Input_TextChanged);
            this.Added = 1;

        }



        private void Input_TextChanged(System.Windows.Controls.TextChangedEventArgs e)
        {
            this.Capacity = stream.NumberOfFiles + this.added;
        }
        /// <summary>
        /// 提交按钮事件
        /// </summary>
        protected override void Execute_Submit()
        {
            var list = new List<DataBlock>();
            for (int i = 0; i < this.Added; i++)
            {
                var block = new DataBlock();
                block.Index = -1;
                list.Add(block);
            }
            this.stream.Import(list);
            this.DialogResult = true;
        }


        protected override Boolean Can_Submit()
        {
            return true;
        }


        public UInt32 Added
        {
            get
            {
                return this.added;
            }
            set
            {
                base.SetProperty(ref this.added, value);
            }
        }

        private UInt32 added;


        public UInt32 Capacity
        {
            get
            {
                return this.capacity;
            }
            set
            {
                base.SetProperty(ref this.capacity, value);
            }
        }

        private UInt32 capacity;

    }
}
