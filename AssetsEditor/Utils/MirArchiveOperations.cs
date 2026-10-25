using Mir2ResourceParser;
using Resource.Package.Assets;
using Resource.Package.Assets.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Assets.Editor.Utils;

internal sealed record ResourceOperationProgress(int Completed, int Total, string Status);

internal static class MirArchiveOperations
{
    internal const string FileFilter = "Wil/Wzl 资源包|*.wil;*.wzl;*.wix;*.wzx";
    private const int BatchFrames = 256;
    private const long BatchBytes = 16 * 1024 * 1024;

    internal static string ExtractionPath(string source, string directory) =>
        Path.Combine(Path.GetFullPath(directory), Path.GetFileNameWithoutExtension(source));

    internal static void Extract(string source, string directory, string palette, AlphaMode alpha,
        IProgress<ResourceOperationProgress> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var archive = MirArchive.Open(source, palettePath: palette);
        string destination = ExtractionPath(source, directory);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"输出目录已存在，请选择其他位置：{destination}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(staging);
        try
        {
            var reporter = new Reporter(progress);
            using (var file = File.Create(Path.Combine(staging, "schema.json")))
            using (var json = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartObject();
                for (int i = 0; i < archive.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var frame = ReadFrame(archive, i, alpha);
                    var info = frame?.Info ?? archive.GetInfo(i);
                    string name = i.ToString("D7", CultureInfo.InvariantCulture) + ".png";
                    SavePng(frame, Path.Combine(staging, name));
                    json.WritePropertyName(name);
                    json.WriteStartObject();
                    json.WriteNumber("width", frame?.Width ?? 0);
                    json.WriteNumber("height", frame?.Height ?? 0);
                    json.WriteNumber("offsetX", info.OffsetX);
                    json.WriteNumber("offsetY", info.OffsetY);
                    json.WriteNumber("lpRenderType", (int)RenderTypes.Normal);
                    json.WriteNumber("unknown1", 0);
                    json.WriteNumber("unknown2", 0);
                    json.WriteEndObject();
                    reporter.Report(i + 1, archive.Count, $"正在解包：{name}");
                }
                json.WriteEndObject();
            }
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            progress?.Report(new(archive.Count, archive.Count, $"解包完成：{destination}"));
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    internal static void CreateAsset(string source, string destination, string password, bool compress,
        string palette, AlphaMode alpha, bool overwrite, IProgress<ResourceOperationProgress> progress,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        destination = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(destination), ".asset", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("输出文件必须使用 .asset 扩展名。");
        if (File.Exists(destination) && !overwrite) throw new IOException("输出文件已存在。");
        using var archive = MirArchive.Open(source, palettePath: palette);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var target = AssetFileStream.Create(staging, password,
                compress ? CompressionOption.MuchPossibleCompress : CompressionOption.NeverCompress))
            {
                Import(archive, target, 0, alpha, progress, token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(staging, destination, overwrite);
            progress?.Report(new(archive.Count, archive.Count, $"转换完成：{destination}"));
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    // null appends; otherwise frame zero maps to startIndex. Empty frames also occupy a slot.
    internal static void ImportToAsset(string source, AssetFileStream target, int? startIndex,
        IProgress<ResourceOperationProgress> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var archive = MirArchive.Open(source);
        Import(archive, target, startIndex, AlphaMode.Preserve, progress, token);
    }

    private static void Import(MirArchive archive, AssetFileStream target, int? startIndex, AlphaMode alpha,
        IProgress<ResourceOperationProgress> progress, CancellationToken token)
    {
        long end = (startIndex ?? (long)target.NumberOfFiles) + archive.Count;
        if (startIndex < 0 || end > int.MaxValue) throw new InvalidDataException("导入后的资源编号超出 asset 支持范围。");
        var batch = new List<DataBlock>(BatchFrames);
        var reporter = new Reporter(progress);
        long bytes = 0;
        for (int i = 0; i < archive.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var frame = ReadFrame(archive, i, alpha);
            var info = frame?.Info ?? archive.GetInfo(i);
            byte[] pixels = frame?.Pixels ?? Array.Empty<byte>();
            if (pixels.Length > 0) AlphaUtil.PremultiplyAlpha(pixels);
            batch.Add(new DataBlock
            {
                Index = startIndex.HasValue ? checked(startIndex.Value + i) : -1,
                Width = frame?.Width ?? 0, Height = frame?.Height ?? 0,
                OffsetX = checked((short)info.OffsetX), OffsetY = checked((short)info.OffsetY),
                lpType = frame is null ? ImageTypes.Unknown : ImageTypes.PNG,
                lpRenderType = RenderTypes.Normal, Data = pixels
            });
            bytes += pixels.Length;
            reporter.Report(i + 1, archive.Count, $"正在读取帧：{i}");
            if (batch.Count >= BatchFrames || bytes >= BatchBytes || i + 1 == archive.Count)
            {
                token.ThrowIfCancellationRequested();
                reporter.Report(i + 1, archive.Count, "正在写入 asset…", force: true);
                target.Import(batch);
                batch.Clear();
                bytes = 0;
                token.ThrowIfCancellationRequested();
            }
        }
    }

    private static ResourceFrame ReadFrame(MirArchive archive, int index, AlphaMode alpha)
    {
        try { return archive.ReadFrame(index, alpha); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or OverflowException)
        { throw new InvalidDataException($"帧 {index} 读取失败：{ex.Message}", ex); }
    }

    private static void SavePng(ResourceFrame frame, string path)
    {
        byte[] pixels = frame?.Pixels ?? new byte[4];
        // Mir2Parser returns straight RGBA; WPF's PNG encoder accepts straight BGRA.
        AlphaUtil.SwitchRedBlue(pixels);
        int width = frame?.Width ?? 1, height = frame?.Height ?? 1;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, checked(width * 4));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private sealed class Reporter(IProgress<ResourceOperationProgress> progress)
    {
        private readonly Stopwatch timer = Stopwatch.StartNew();
        internal void Report(int completed, int total, string status, bool force = false)
        {
            if (!force && completed != total && timer.ElapsedMilliseconds < 80) return;
            progress?.Report(new(completed, total, status));
            timer.Restart();
        }
    }
}
