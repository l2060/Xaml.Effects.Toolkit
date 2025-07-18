﻿using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;


namespace Xaml.Effects.Toolkit.Effects
{

    public class LongShadowEffect : ShaderEffect
    {
        public static readonly DependencyProperty InputProperty = ShaderEffect.RegisterPixelShaderSamplerProperty("Input", typeof(LongShadowEffect), 0);
        public static readonly DependencyProperty ShadowLengthProperty = DependencyProperty.Register("ShadowLength", typeof(double), typeof(LongShadowEffect), new UIPropertyMetadata(((double)(100D)), PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty OpacityProperty = DependencyProperty.Register("Opacity", typeof(double), typeof(LongShadowEffect), new UIPropertyMetadata(((double)(1D)), PixelShaderConstantCallback(1)));
        public static readonly DependencyProperty WidthProperty = DependencyProperty.Register("Width", typeof(double), typeof(LongShadowEffect), new UIPropertyMetadata(((double)(0D)), PixelShaderConstantCallback(2)));
        public static readonly DependencyProperty HeightProperty = DependencyProperty.Register("Height", typeof(double), typeof(LongShadowEffect), new UIPropertyMetadata(((double)(0D)), PixelShaderConstantCallback(3)));
        public static readonly DependencyProperty ColorProperty = DependencyProperty.Register("Color", typeof(Color), typeof(LongShadowEffect), new UIPropertyMetadata(Color.FromArgb(255, 0, 0, 0), PixelShaderConstantCallback(4)));
        public LongShadowEffect()
        {
            PixelShader pixelShader = new PixelShader();
            pixelShader.UriSource = new Uri("/Xaml.Effects.Toolkit;component/Shaders/LongShadowEffect.ps", UriKind.Relative);
            this.PixelShader = pixelShader;

            this.UpdateShaderValue(InputProperty);
            this.UpdateShaderValue(ShadowLengthProperty);
            this.UpdateShaderValue(OpacityProperty);
            this.UpdateShaderValue(WidthProperty);
            this.UpdateShaderValue(HeightProperty);
            this.UpdateShaderValue(ColorProperty);
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
        /// <summary>ShadowLength.</summary>
        public double ShadowLength
        {
            get
            {
                return ((double)(this.GetValue(ShadowLengthProperty)));
            }
            set
            {
                this.SetValue(ShadowLengthProperty, value);
            }
        }
        /// <summary>Opacity.</summary>
        public double Opacity
        {
            get
            {
                return ((double)(this.GetValue(OpacityProperty)));
            }
            set
            {
                this.SetValue(OpacityProperty, value);
            }
        }
        /// <summary>Opacity.</summary>
        public double Width
        {
            get
            {
                return ((double)(this.GetValue(WidthProperty)));
            }
            set
            {
                this.SetValue(WidthProperty, value);
            }
        }
        /// <summary>Opacity.</summary>
        public double Height
        {
            get
            {
                return ((double)(this.GetValue(HeightProperty)));
            }
            set
            {
                this.SetValue(HeightProperty, value);
            }
        }
        /// <summary>Color.</summary>
        public Color Color
        {
            get
            {
                return ((Color)(this.GetValue(ColorProperty)));
            }
            set
            {
                this.SetValue(ColorProperty, value);
            }
        }
    }
}
