﻿using Assets.Editor.Models;
using System.Windows;

namespace Assets.Editor.Views
{
    /// <summary>
    /// PasswordInput.xaml 的交互逻辑
    /// </summary>
    public partial class BatchOptimizeDialog : Window
    {

        public BatchOptimizeModel Model { get; set; } = new BatchOptimizeModel();

        public BatchOptimizeDialog()
        {
            this.DataContext = Model;
            InitializeComponent();
            this.Model.OnClose += Model_OnClose;
        }

        private void Model_OnClose(object sender, Xaml.Effects.Toolkit.Model.WindowDestroyArgs e)
        {
            this.DialogResult = e.DialogResult;
        }

    }
}
