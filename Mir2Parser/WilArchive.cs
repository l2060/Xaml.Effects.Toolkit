using System.Buffers.Binary;

namespace Mir2ResourceParser;

public sealed class WilArchive : MirArchive
{
    protected override int MinimumOffset => 56;
    public int BitsPerPixel { get; }

    public WilArchive(string path, string indexPath, bool includeTrailingIndices = false)
        : base(path, indexPath, includeTrailingIndices)
    {
        try
        {
            var h = new byte[56];
            Stream.ReadExactly(h);
            int colors = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(48));
            BitsPerPixel = colors switch { 256 => 8, 65536 => 16, -1 => 32,
                _ => throw new NotSupportedException($"未知 WIL 颜色格式：{colors}。") };
            if (BitsPerPixel == 8)
            {
                var palette = new byte[1024];
                Stream.ReadExactly(palette);
                Palette = MirPalette.Decode(palette);
            }
        }
        catch { Dispose(); throw; }
    }

    public override ImageInfo GetInfo(int index)
    {
        var h = ReadHeader(index, 8);
        if (h.Length == 0) return new(index, 0, 0, 0, 0, BitsPerPixel, 0, 0, false);
        int width = BinaryPrimitives.ReadInt16LittleEndian(h);
        int height = BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(2));
        int x = BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(4));
        int y = BinaryPrimitives.ReadInt16LittleEndian(h.AsSpan(6));
        if (width < 0 || height < 0) throw new InvalidDataException($"帧 {index} 尺寸为负。");
        int length = checked(PixelDecoder.Stride(width, BitsPerPixel) * height);
        // 某些编辑器用仅有图片头的 1x1 记录占位。
        if (FrameEnd(index) == Index.Offsets[index] + 8L) length = 0;
        return new(index, width, height, x, y, BitsPerPixel, Index.Offsets[index] + 8L, length, false);
    }
}
