using System.Buffers.Binary;

namespace Mir2ResourceParser;

/// <summary>WZL 文件头：前 44 字节保留，第 44 字节起为无符号图片计数。</summary>
public sealed record WzlHeader(uint ImageCount)
{
    public const int Size = 48;

    public static WzlHeader Read(Stream stream)
    {
        stream.Position = 0;
        Span<byte> bytes = stackalloc byte[Size];
        stream.ReadExactly(bytes);
        return new(BinaryPrimitives.ReadUInt32LittleEndian(bytes[44..]));
    }
}
