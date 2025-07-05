using System;
using System.Drawing;
using System.Windows;
using System.Windows.Media.Effects;

namespace Xaml.Effects.Toolkit.Effects
{
    public class GlowEffect : ShaderEffect
    {
        public static readonly DependencyProperty InputProperty = ShaderEffect.RegisterPixelShaderSamplerProperty("Input", typeof(GlowEffect), 0);
        public static readonly DependencyProperty AmountProperty = DependencyProperty.Register("Amount", typeof(double), typeof(GlowEffect), new UIPropertyMetadata(((double)(1D)), PixelShaderConstantCallback(0)));
        public GlowEffect()
        {
            PixelShader pixelShader = new PixelShader();
            pixelShader.UriSource = new Uri("/Xaml.Effects.Toolkit;component/Shaders/GlowEffect.ps", UriKind.Relative);
            this.PixelShader = pixelShader;

            this.UpdateShaderValue(InputProperty);
            this.UpdateShaderValue(AmountProperty);
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

        public double Amount
        {
            get
            {
                return ((double)(this.GetValue(AmountProperty)));
            }
            set
            {
                this.SetValue(AmountProperty, value);
            }
        }
    }
}
