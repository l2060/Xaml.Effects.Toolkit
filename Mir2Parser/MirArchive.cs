using System.IO.Compression;

namespace Mir2ResourceParser;

/// <summary>一个实例持有一个只读流和索引，不支持并发读取。</summary>
public abstract class MirArchive : IDisposable
{
    protected FileStream Stream { get; }
    protected ReadOnlyMemory<Rgba32> Palette { get; set; } = MirPalette.Default;
    public ResourceIndex Index { get; }
    public int Count => Index.Count;
    private readonly long[] ends;
    protected abstract int MinimumOffset { get; }

    protected MirArchive(string resourcePath, string indexPath, bool includeTrailingIndices = false)
    {
        Index = new ResourceIndex(indexPath, includeTrailingIndices);
        Stream = File.OpenRead(resourcePath);
        var sorted = Index.Offsets.Where(x => x > 0).Distinct().Order().Select(x => (long)x).ToArray();
        ends = new long[Count];
        for (int i = 0; i < Count; i++)
        {
            int p = Array.BinarySearch(sorted, (long)Index.Offsets[i]);
            ends[i] = p >= 0 && p + 1 < sorted.Length ? sorted[p + 1] : Stream.Length;
        }
    }

    public static MirArchive Open(string path, string? indexPath = null, string? palettePath = null,
        bool includeTrailingIndices = false)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".wix" or ".wzx")
        {
            indexPath ??= path;
            path = ResourcePaths.Companion(path, ext == ".wix" ? ".wil" : ".wzl");
            ext = Path.GetExtension(path).ToLowerInvariant();
        }
        indexPath = string.IsNullOrWhiteSpace(indexPath) ? ResourcePaths.Companion(path, ext == ".wil" ? ".wix" : ".wzx") : indexPath;
        MirArchive archive = ext switch
        {
            ".wzl" => new WzlArchive(path, indexPath, includeTrailingIndices),
            ".wil" => new WilArchive(path, indexPath, includeTrailingIndices),
            _ => throw new NotSupportedException($"不支持的资源扩展名：{ext}")
        };
        try
        {
            if (!string.IsNullOrWhiteSpace(palettePath)) archive.Palette = MirPalette.Load(palettePath);
            return archive;
        }
        catch { archive.Dispose(); throw; }
    }

    protected long FrameEnd(int index) => Math.Min(ends[index], Stream.Length);

    public long GetPosition(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return Index.Offsets[index];
    }

    protected byte[] ReadHeader(int index, int size, bool useIndexBoundary = true)
    {
        long offset = GetPosition(index);
        if (offset == 0) return [];
        long end = useIndexBoundary ? FrameEnd(index) : Stream.Length;
        if (offset < MinimumOffset || offset + size > end)
            throw new InvalidDataException($"帧 {index} 的文件偏移或图片头无效：{offset}。");
        Stream.Position = offset;
        var bytes = new byte[size];
        Stream.ReadExactly(bytes);
        return bytes;
    }

    public abstract ImageInfo GetInfo(int index);

    public ResourceImageData? ReadImageData(int index)
        => ReadImageData(index, GetInfo(index));

    private ResourceImageData? ReadImageData(int index, ImageInfo info)
    {
        if (info.IsEmpty) return null;
        int expected = checked(info.PixelDataLength + info.AlphaDataLength);
        if (expected > 256 * 1024 * 1024) throw new InvalidDataException("图片像素超过 256 MiB 限制。");
        // 压缩帧有自己的长度；坏索引可能误指向其内部，不能因此截断合法的 zlib 流。
        long dataEnd = info.Compressed ? Stream.Length : FrameEnd(index);
        if (info.DataLength < 0 || info.DataOffset + info.DataLength > dataEnd)
            throw new InvalidDataException($"帧 {index} 像素数据被截断。");
        Stream.Position = info.DataOffset;
        byte[] pixels = new byte[expected];
        if (info.Compressed)
        {
            if (info.DataLength > 256 * 1024 * 1024) throw new InvalidDataException("压缩数据过大。");
            byte[] compressed = new byte[info.DataLength];
            Stream.ReadExactly(compressed);
            using var input = new MemoryStream(compressed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            zlib.ReadExactly(pixels);
            if (zlib.ReadByte() != -1) throw new InvalidDataException($"帧 {index} 解压长度超出预期。");
        }
        else
        {
            if (info.DataLength != expected) throw new InvalidDataException($"帧 {index} 像素长度错误。");
            Stream.ReadExactly(pixels);
        }
        return new ResourceImageData(info, pixels);
    }

    public ResourceFrame? ReadFrame(int index, AlphaMode alphaMode = AlphaMode.Preserve)
    {
        if (!Enum.IsDefined(alphaMode)) throw new ArgumentOutOfRangeException(nameof(alphaMode));
        var header = GetInfo(index);
        if (!header.IsEmpty && (long)header.Width * header.Height * 4 > PixelDecoder.MaximumDecodedBytes)
            throw new InvalidDataException("RGBA 数据超过 256 MiB 限制。");
        var data = ReadImageData(index, header);
        if (data == null) return null;
        var info = data.Info;
        return new ResourceFrame(info, PixelDecoder.Decode(data.Pixels, info.Width, info.Height,
            info.BitsPerPixel, Palette.Span, info.HasAlpha4, alphaMode));
    }

    public IEnumerable<ResourceFrame> ReadFrames(AlphaMode alphaMode = AlphaMode.Preserve)
    {
        for (int i = 0; i < Count; i++)
        {
            var frame = ReadFrame(i, alphaMode);
            if (frame != null) yield return frame;
        }
    }

    public void Dispose() => Stream.Dispose();
}
