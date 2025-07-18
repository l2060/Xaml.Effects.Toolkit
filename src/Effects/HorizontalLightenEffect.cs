﻿using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;


namespace Xaml.Effects.Toolkit.Effects
{

    public class HorizontalLightenEffect : ShaderEffect
    {
        public static readonly DependencyProperty InputProperty = ShaderEffect.RegisterPixelShaderSamplerProperty("Input", typeof(HorizontalLightenEffect), 0);
        public static readonly DependencyProperty LeftProperty = DependencyProperty.Register("Left", typeof(double), typeof(HorizontalLightenEffect), new UIPropertyMetadata(((double)(0D)), PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty RightProperty = DependencyProperty.Register("Right", typeof(double), typeof(HorizontalLightenEffect), new UIPropertyMetadata(((double)(0D)), PixelShaderConstantCallback(1)));
        public static readonly DependencyProperty CenterProperty = DependencyProperty.Register("Center", typeof(double), typeof(HorizontalLightenEffect), new UIPropertyMetadata(((double)(0D)), PixelShaderConstantCallback(2)));
        public HorizontalLightenEffect()
        {
            PixelShader pixelShader = new PixelShader();
            pixelShader.UriSource = new Uri("/Xaml.Effects.Toolkit;component/Shaders/HorizontalLightenEffect.ps", UriKind.Relative);
            this.PixelShader = pixelShader;

            this.UpdateShaderValue(InputProperty);
            this.UpdateShaderValue(LeftProperty);
            this.UpdateShaderValue(RightProperty);
            this.UpdateShaderValue(CenterProperty);
        }
        public Brush Input
        {
            get
            {
                return ((Brush)(this.GetValue(InputProperty)));
            }
            set
            {
                this.SetValue(InputProperty, value);
            }
        }
        public double Left
        {
            get
            {
                return ((double)(this.GetValue(LeftProperty)));
            }
            set
            {
                this.SetValue(LeftProperty, value);
            }
        }
        public double Right
        {
            get
            {
                return ((double)(this.GetValue(RightProperty)));
            }
            set
            {
                this.SetValue(RightProperty, value);
            }
        }
        public double Center
        {
            get
            {
                return ((double)(this.GetValue(CenterProperty)));
            }
            set
            {
                this.SetValue(CenterProperty, value);
            }
        }
    }
}
