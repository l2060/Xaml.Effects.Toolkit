namespace Mir2ResourceParser;

/// <summary>解压后的原始 DIB 像素，包含行填充和可能存在的 alpha4 平面，不持有文件句柄。</summary>
public sealed record ResourceImageData(ImageInfo Info, byte[] Pixels);
