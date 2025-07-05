using Assets.Editor.Utils;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Win32;
using Resource.Package.Assets;
using Resource.Package.Assets.Common;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Xaml.Effects.Toolkit.Model;

namespace Assets.Editor.Models
{

    public class CreateAssetsInputModel : DialogModel
    {
        public ICommand SelectFileCommand { get; protected set; }




        public CreateAssetsInputModel()
        {
            this.Title = "新建资源包";
            this.FileName = "";
            this.CompressionOption = CompressionOption.MuchPossibleCompress;
            this.SelectFileCommand = new RelayCommand(SelectFiile_Click);
        }

        private void SelectFiile_Click()
        {
            SaveFileDialog sfd = new SaveFileDialog();
            //设置这个对话框的起始保存路径
            sfd.InitialDirectory = ConfigureUtil.GetValue("CreateAssetDirectory");
            //设置保存的文件的类型，注意过滤器的语法
            sfd.Filter = "Assets Package|*.asset";
            //调用ShowDialog()方法显示该对话框，该方法的返回值代表用户是否点击了确定按钮
            if (sfd.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(sfd.FileName);
                ConfigureUtil.SetValue("CreateAssetDirectory", dir);
                this.FileName = sfd.FileName;
            }
            this.SubmitCommand.NotifyCanExecuteChanged();
        }


        /// <summary>
        /// 提交按钮事件
        /// </summary>
        protected override void Execute_Submit()
        {
            try
            {
                var file = AssetFileStream.Create(this.FileName, this.Password, this.CompressionOption);
                file.Dispose();
                this.DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建失败：{ex.Message}");
            }
        }


        protected override Boolean Can_Submit()
        {
            return !String.IsNullOrEmpty(this.FileName);
        }





        public String Password
        {
            get
            {
                return this.password;
            }
            set
            {
                base.SetProperty(ref this.password, value);
            }
        }

        private String password;


        public String FileName
        {
            get
            {
                return this.fileName;
            }
            set
            {
                base.SetProperty(ref this.fileName, value);
            }
        }

        private String fileName;




        public CompressionOption CompressionOption
        {
            get
            {
                return this.compressionOption;
            }
            set
            {
                base.SetProperty(ref this.compressionOption, value);
            }
        }

        private CompressionOption compressionOption;
    }
}
