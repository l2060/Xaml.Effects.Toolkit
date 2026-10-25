namespace Mir2ResourceParser;

public static class PixelDecoder
{
    public const int MaximumDecodedBytes = 256 * 1024 * 1024;

    /// <summary>源 DIB 的四字节对齐行跨度，不是输出 RGBA 的行跨度。</summary>
    public static int Stride(int width, int bits) => checked((checked(width * bits) + 31) / 32 * 4);

    /// <summary>将源 DIB 转为紧密排列、自上而下的 RGBA32 非预乘字节。</summary>
    public static byte[] Decode(ReadOnlySpan<byte> pixels, int width, int height, int bits,
        ReadOnlySpan<Rgba32> palette, bool alpha4 = false, AlphaMode alphaMode = AlphaMode.Preserve)
    {
        if (!Enum.IsDefined(alphaMode)) throw new ArgumentOutOfRangeException(nameof(alphaMode));
        if (width <= 0 || height <= 0 || bits is not (8 or 16 or 24 or 32))
            throw new InvalidDataException("不支持的图片尺寸或位深。");
        if (bits == 8 && palette.Length != 256) throw new InvalidDataException("索引色需要 256 个调色板条目。");
        long outputLength = (long)width * height * 4;
        if (outputLength > MaximumDecodedBytes) throw new InvalidDataException("RGBA 数据超过 256 MiB 限制。");
        int stride = Stride(width, bits);
        int alphaStride = (width + 1) / 2;
        if (pixels.Length != checked(stride * height + (alpha4 ? alphaStride * height : 0)))
            throw new InvalidDataException("像素长度与尺寸不匹配。");

        var rgba = new byte[(int)outputLength];
        for (int y = 0; y < height; y++)
        {
            int start = (height - 1 - y) * stride;
            for (int x = 0; x < width; x++)
            {
                int p = start + x * (bits / 8);
                int r, g, b, a = 255;
                switch (bits)
                {
                    case 8:
                        var c = palette[pixels[p]];
                        r = c.R; g = c.G; b = c.B;
                        if (pixels[p] == 0) a = 0;
                        break;
                    case 16:
                        int v = pixels[p] | pixels[p + 1] << 8;
                        r = ((v >> 11) & 31) * 255 / 31;
                        g = ((v >> 5) & 63) * 255 / 63;
                        b = (v & 31) * 255 / 31;
                        if (v == 0) a = 0;
                        break;
                    default:
                        b = pixels[p]; g = pixels[p + 1]; r = pixels[p + 2];
                        a = bits == 32 ? pixels[p + 3] : (r == 0 && g == 0 && b == 0 ? 0 : 255);
                        break;
                }
                if (alpha4)
                {
                    // 颜色自下而上；附加 alpha 平面自上而下，高半字节在左。
                    int alpha = pixels[stride * height + y * alphaStride + x / 2];
                    a = ((alpha >> (x % 2 == 0 ? 4 : 0)) & 15) * 17;
                }
                int output = (y * width + x) * 4;
                rgba[output] = (byte)r;
                rgba[output + 1] = (byte)g;
                rgba[output + 2] = (byte)b;
                rgba[output + 3] = (byte)a;
            }
        }
        if (alphaMode == AlphaMode.FromBlackBackground && bits != 32 && !alpha4)
            AlphaConverter.ConvertInPlace(rgba);
        return rgba;
    }
}
