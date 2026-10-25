namespace Mir2ResourceParser;

/// <summary>托管像素数据，无图片对象或非托管句柄，不需要 Dispose。</summary>
public sealed class ResourceFrame
{
    public ImageInfo Info { get; }
    public int Width => Info.Width;
    public int Height => Info.Height;
    public int Stride => checked(Width * 4);

    /// <summary>
    /// RGBA32：每像素依次 R、G、B、A，自上而下，行内无填充，非预乘 alpha。
    /// 长度固定 Width * Height * 4。数组由本帧持有，允许调用者修改；关闭归档后仍然有效。
    /// </summary>
    public byte[] Pixels { get; }

    public ResourceFrame(ImageInfo info, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(pixels);
        if (info.Width <= 0 || info.Height <= 0)
            throw new ArgumentException("帧尺寸必须为正。", nameof(info));
        if (pixels.Length != checked(info.Width * info.Height * 4))
            throw new ArgumentException("RGBA32 字节长度必须为 Width * Height * 4。", nameof(pixels));
        Info = info;
        Pixels = pixels;
    }
}
