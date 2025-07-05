using System;
using System.Windows;
using System.Windows.Controls;

namespace Xaml.Effects.Toolkit.Controls
{
    /// <summary>
    /// 九宫格背景Border
    /// </summary>
    public class SplitButton : ContentControl
    {
        /// <summary>
        /// 图片源
        /// </summary>
        public String IconFont
        {
            get { return (String)base.GetValue(IconFontProperty); }
            set { base.SetValue(IconFontProperty, value); }
        }

        /// <summary>
        /// 图片源
        /// </summary>
        public static readonly DependencyProperty IconFontProperty = DependencyProperty.Register("IconFont",
        typeof(String), typeof(SplitButton),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));









        ///// <summary>
        ///// 图片四个边距
        ///// </summary>
        public Boolean IsPressed
        {
            get { return (Boolean)GetValue(IsPressedProperty); }
            set { SetValue(IsPressedProperty, value); }
        }

        /// <summary>
        /// 图片四个边距
        /// </summary>
        public static readonly DependencyProperty IsPressedProperty =
        DependencyProperty.Register("IsPressed", typeof(Boolean), typeof(SplitButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    }
}
