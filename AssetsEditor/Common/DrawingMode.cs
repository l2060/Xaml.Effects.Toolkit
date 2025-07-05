using System.ComponentModel;

namespace Assets.Editor.Common
{
    public enum DrawingMode
    {
        [Description("原始图像")]
        Raw = 0,
        [Description("去除底色")]
        MaskColor = 1,
        [Description("Alpha混合")]
        AlphaBlend = 2,
        [Description("反预乘Alpha")]
        UnpremultiplyAlpha = 3,
    }
}
