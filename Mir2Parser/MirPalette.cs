namespace Mir2ResourceParser;

public static class MirPalette
{
    public static ReadOnlyMemory<Rgba32> Default { get; } = LoadDefault();

    private static Rgba32[] LoadDefault()
    {
        using var stream = typeof(MirPalette).Assembly.GetManifestResourceStream("Mir2Parser.Resources.mir2.pal")!;
        var bytes = new byte[768];
        stream.ReadExactly(bytes);
        return Decode(bytes);
    }

    public static Rgba32[] Load(string path) => Decode(File.ReadAllBytes(path));

    public static Rgba32[] Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 768 && bytes.Length != 1024)
            throw new InvalidDataException("调色板应为 768 字节 RGB 或 1024 字节 BGRA。");
        var colors = new Rgba32[256];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = bytes.Length == 768
                ? new Rgba32(bytes[i * 3], bytes[i * 3 + 1], bytes[i * 3 + 2])
                : new Rgba32(bytes[i * 4 + 2], bytes[i * 4 + 1], bytes[i * 4]);
        return colors;
    }
}
