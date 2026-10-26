using System.Buffers.Binary;

namespace Game.Asset.Package.Internal;

internal readonly struct ContentDigest(ReadOnlySpan<byte> bytes)
{
    private readonly ulong _a = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    private readonly ulong _b = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
    private readonly ulong _c = BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]);
    private readonly ulong _d = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]);
    internal bool IsZero => (_a | _b | _c | _d) == 0;
    internal void WriteTo(Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, _a); BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], _b);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], _c); BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], _d);
    }
    internal bool Matches(ReadOnlySpan<byte> bytes) => bytes.Length == 32 &&
        ((_a ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes)) | (_b ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..])) |
         (_c ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..])) | (_d ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]))) == 0;
    public static implicit operator ContentDigest(byte[] bytes) => new(bytes);
}
