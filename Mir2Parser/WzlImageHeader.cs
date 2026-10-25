using System.Buffers.Binary;

namespace Mir2ResourceParser;

/// <summary>16 字节图片头。标准 Depth 为 0x0103（索引色）或 0x0105（RGB565）。</summary>
public sealed record WzlImageHeader(ushort Depth, ushort Reserved, ushort Width, ushort Height,
    short OffsetX, short OffsetY, uint StoredLength)
{
    public const int Size = 16;
    public byte PixelFormat => (byte)Depth;
    public byte Flags => (byte)(Depth >> 8);
    public uint Sign => (uint)(ushort)OffsetX | ((uint)(ushort)OffsetY << 16);

    public static WzlImageHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size) throw new InvalidDataException("WZL 图片头不足 16 字节。");
        return new(BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
            BinaryPrimitives.ReadInt16LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadInt16LittleEndian(bytes[10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]));
    }
}
