using System.Buffers.Binary;

namespace Mir2ResourceParser;

public sealed class ResourceIndex
{
    public int DeclaredCount { get; }
    public int AvailableCount { get; }
    public int TrailingByteCount { get; }
    public IReadOnlyList<uint> Offsets { get; }
    public int Count => Offsets.Count;

    public ResourceIndex(string path, bool includeTrailingIndices = false)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 48)
            throw new InvalidDataException("索引文件头不足 48 字节。");
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(44));
        AvailableCount = (bytes.Length - 48) / 4;
        if (declared > AvailableCount)
            throw new InvalidDataException($"索引被截断：声明 {declared} 项，实际最多 {AvailableCount} 项。");
        DeclaredCount = (int)declared;
        TrailingByteCount = bytes.Length - 48 - DeclaredCount * 4;
        // 与 king-go 一致：以声明数量为准，尾部可能是旧索引或其他数据。
        if (includeTrailingIndices && (bytes.Length - 48) % 4 != 0)
            throw new InvalidDataException("扩展索引模式要求尾部按 4 字节对齐。");
        int count = includeTrailingIndices ? AvailableCount : DeclaredCount;
        var offsets = new uint[count];
        for (int i = 0; i < count; i++)
        {
            offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48 + i * 4));
        }
        Offsets = Array.AsReadOnly(offsets);
    }
}
