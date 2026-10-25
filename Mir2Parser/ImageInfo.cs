namespace Mir2ResourceParser;

public sealed record ImageInfo(int Index, int Width, int Height, int OffsetX, int OffsetY,
    int BitsPerPixel, long DataOffset, int DataLength, bool Compressed, byte Flags = 0)
{
    public bool IsEmpty => Width == 0 || Height == 0 || DataLength == 0;
    public uint Sign => (uint)(ushort)OffsetX | ((uint)(ushort)OffsetY << 16);
    public bool HasAlpha4 => (Flags & 8) != 0;
    public int PixelDataLength => checked(PixelDecoder.Stride(Width, BitsPerPixel) * Height);
    public int AlphaDataLength => HasAlpha4 ? checked((Width + 1) / 2 * Height) : 0;
}
