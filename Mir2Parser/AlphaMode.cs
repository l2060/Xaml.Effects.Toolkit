namespace Mir2ResourceParser;

public enum AlphaMode
{
    /// <summary>保留资源 alpha；无 alpha 的图片只使用原有透明色规则。</summary>
    Preserve,

    /// <summary>对没有原生 alpha 的黑底特效生成渐变 alpha，并补偿 RGB。</summary>
    FromBlackBackground
}
