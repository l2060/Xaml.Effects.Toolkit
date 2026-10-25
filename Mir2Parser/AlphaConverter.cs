namespace Mir2ResourceParser;

/// <summary>将黑底颜色强度转换为非预乘 RGBA 渐变 alpha，纯托管实现。</summary>
public static class AlphaConverter
{
    /// <summary>返回新的 RGBA32 数组，保留输入数据；输入长度必须是 4 的倍数。</summary>
    public static byte[] FromBlackBackground(ReadOnlySpan<byte> rgba)
    {
        ValidateLength(rgba.Length);
        byte[] result = rgba.ToArray();
        ConvertInPlace(result);
        return result;
    }

    /// <summary>
    /// 就地转换 RGBA32。A=max(R,G,B)，RGB 除以 A，已有 alpha 作为覆盖率相乘。
    /// 保持黑底合成结果（允许量化误差），不等同于任意背景上的加法混合。
    /// </summary>
    public static void ConvertInPlace(Span<byte> rgba)
    {
        ValidateLength(rgba.Length);
        for (int p = 0; p < rgba.Length; p += 4)
        {
            int r = rgba[p], g = rgba[p + 1], b = rgba[p + 2], a = rgba[p + 3];
            int strength = Math.Max(r, Math.Max(g, b));
            int alpha = (a * strength + 127) / 255;
            if (alpha == 0)
            {
                rgba.Slice(p, 4).Clear();
                continue;
            }
            rgba[p] = (byte)((r * 255 + strength / 2) / strength);
            rgba[p + 1] = (byte)((g * 255 + strength / 2) / strength);
            rgba[p + 2] = (byte)((b * 255 + strength / 2) / strength);
            rgba[p + 3] = (byte)alpha;
        }
    }

    private static void ValidateLength(int length)
    {
        if (length % 4 != 0) throw new ArgumentException("RGBA32 数据长度必须是 4 的倍数。", "rgba");
    }
}
