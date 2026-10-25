namespace Mir2ResourceParser;

public sealed class WzlArchive : MirArchive
{
    protected override int MinimumOffset => WzlHeader.Size;
    public WzlHeader Header { get; }

    public WzlArchive(string path, string? indexPath = null, bool includeTrailingIndices = false)
        : base(path, indexPath ?? ResourcePaths.Companion(path, ".wzx"), includeTrailingIndices)
    {
        try { Header = WzlHeader.Read(Stream); }
        catch { Dispose(); throw; }
    }

    public WzlImageHeader? GetImageHeader(int index)
    {
        // WZL 帧自带长度，不依赖索引排序，也不让坏索引截断另一个合法图片头。
        var bytes = ReadHeader(index, WzlImageHeader.Size, useIndexBoundary: false);
        return bytes.Length == 0 ? null : WzlImageHeader.Parse(bytes);
    }

    public override ImageInfo GetInfo(int index)
    {
        var h = GetImageHeader(index);
        if (h == null) return new(index, 0, 0, 0, 0, 0, 0, 0, false);
        long dataOffset = GetPosition(index) + WzlImageHeader.Size;
        if (h.Width == 0 || h.Height == 0)
            return new(index, h.Width, h.Height, h.OffsetX, h.OffsetY, 0, dataOffset, 0, false, h.Flags);
        int bits = h.PixelFormat switch
        {
            3 => 8, 5 => 16, 6 => 24, 7 => 32,
            _ => throw new NotSupportedException($"帧 {index} 使用未知 WZL 像素格式 0x{h.Depth:X4}。")
        };
        if (h.StoredLength > int.MaxValue)
            throw new InvalidDataException($"帧 {index} 数据长度过大：{h.StoredLength}。");
        int length = (int)h.StoredLength;
        // 编辑器留下的全黑 1x1 占位：有 alpha 标志，但只有颜色行而没有 alpha。
        if (length == 0 && bits == 16 && h.Width == 1 && h.Height == 1 && (h.Flags & 8) != 0
            && FrameEnd(index) == dataOffset + 4 && Stream.ReadByte() == 0 && Stream.ReadByte() == 0)
            return new(index, h.Width, h.Height, h.OffsetX, h.OffsetY, bits, dataOffset, 0, true, h.Flags);
        bool compressed = length > 0;
        // 部分客户端不压缩的图片仍带标志 1，按长度而非标志决定是否解压。
        if (!compressed)
            length = checked(PixelDecoder.Stride(h.Width, bits) * h.Height
                + ((h.Flags & 8) != 0 ? (h.Width + 1) / 2 * h.Height : 0));
        return new(index, h.Width, h.Height, h.OffsetX, h.OffsetY, bits, dataOffset, length, compressed, h.Flags);
    }
}
