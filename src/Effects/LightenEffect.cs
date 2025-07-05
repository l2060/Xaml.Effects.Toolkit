using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;


namespace Xaml.Effects.Toolkit.Effects
{

    public class LightenEffect : ShaderEffect
    {
        public static readonly DependencyProperty InputProperty = ShaderEffect.RegisterPixelShaderSamplerProperty("Input", typeof(LightenEffect), 0);
        public static readonly DependencyProperty DeltaProperty = DependencyProperty.Register("Delta", typeof(double), typeof(LightenEffect), new UIPropertyMetadata(((double)(0D)), PixelShaderConstantCallback(0)));
        public LightenEffect()
        {
            PixelShader pixelShader = new PixelShader();
            pixelShader.UriSource = new Uri("/Xaml.Effects.Toolkit;component/Shaders/LightenEffect.ps", UriKind.Relative);
            this.PixelShader = pixelShader;

            this.UpdateShaderValue(InputProperty);
            this.UpdateShaderValue(DeltaProperty);
        }
        private Brush Input
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
        public double Delta
        {
            get
            {
                return ((double)(this.GetValue(DeltaProperty)));
            }
            set
            {
                this.SetValue(DeltaProperty, value);
            }
        }
    }
}
