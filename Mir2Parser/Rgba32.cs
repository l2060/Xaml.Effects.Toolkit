namespace Mir2ResourceParser;

/// <summary>与平台和机器字节序无关的 RGBA 通道值，供调色板使用。</summary>
public readonly record struct Rgba32(byte R, byte G, byte B, byte A = 255)
{
    public int ToArgb() => (A << 24) | (R << 16) | (G << 8) | B;
    public static Rgba32 FromArgb(int a, int r, int g, int b) =>
        new(checked((byte)r), checked((byte)g), checked((byte)b), checked((byte)a));
    public static Rgba32 FromArgb(int r, int g, int b) => FromArgb(255, r, g, b);
}
