using Assets.Editor.Models;
using Assets.Editor.Utils;
using Mir2ResourceParser;
using Resource.Package.Assets;
using Resource.Package.Assets.Common;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class MirArchiveChecks
{
    private static readonly byte[] Rgba = [255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 64, 0, 0, 0, 0];
    private static readonly byte[] Premultiplied = [255, 0, 0, 255, 0, 128, 0, 128, 0, 0, 64, 64, 0, 0, 0, 0];

    internal static void Run(string output)
    {
        // Each run owns a fresh directory, never modifies external game resources.
        string root = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (bool wzl in new[] { false, true })
        {
            string source = Fixture(root, wzl);
            var reports = new List<ResourceOperationProgress>();
            var progress = new InlineProgress(p => reports.Add(p));
            string destination = source + ".asset";
            // Asset compression must be tested separately from WZL's source zlib compression.
            foreach (bool compress in new[] { false, true })
            {
                MirArchiveOperations.CreateAsset(source, destination, "test-password", compress,
                    null, AlphaMode.Preserve, true, progress, default);
                using var asset = AssetFileStream.Open(destination, "test-password");
                VerifyFrames(asset, 0);
                Require(asset.NumberOfFiles == 4, "Frame count changed.");
            }
            Require(reports.Count > 0 && reports[^1].Completed == 4, "Progress did not finish.");

            string exportRoot = Path.Combine(root, "export");
            MirArchiveOperations.Extract(source, exportRoot, null, AlphaMode.Preserve, progress, default);
            string export = MirArchiveOperations.ExtractionPath(source, exportRoot);
            Require(Directory.GetFiles(export, "*.png").Length == 4, "Missing empty-frame PNG placeholders.");
            var decoded = ReadPng(Path.Combine(export, "0000000.png"));
            Require(decoded.Width == 2 && decoded.Height == 2 && decoded.Pixels.SequenceEqual(Rgba), "PNG orientation/channels/alpha changed.");
            var empty = ReadPng(Path.Combine(export, "0000001.png"));
            Require(empty.Width == 1 && empty.Pixels.All(b => b == 0), "Empty PNG is not transparent.");
            using (var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(export, "schema.json"))))
            {
                Require(metadata.RootElement.EnumerateObject().Count() == 4, "Schema dropped empty frames.");
                var first = metadata.RootElement.GetProperty("0000000.png");
                Require(first.GetProperty("offsetX").GetInt32() == -17 && first.GetProperty("offsetY").GetInt32() == 9, "Offsets changed.");
            }
            Throws<IOException>(() => MirArchiveOperations.Extract(source, exportRoot, null, AlphaMode.Preserve, null, default));

            // Appending, overwriting from frame zero, and replacing from a selected destination slot.
            using (var target = AssetFileStream.Create(source + ".import.asset", "", CompressionOption.MuchPossibleCompress))
            {
                target.Import([new DataBlock { Index = -1 }]);
                MirArchiveOperations.ImportToAsset(source, target, null, null, default);
                Require(target.NumberOfFiles == 5, "Append overwrote an existing slot.");
                VerifyFrames(target, 1);
                MirArchiveOperations.ImportToAsset(source, target, 0, null, default);
                VerifyFrames(target, 0);
                Require(target.NumberOfFiles == 5, "Overwrite truncated unrelated resources.");
                MirArchiveOperations.ImportToAsset(source, target, 3, null, default);
                VerifyFrames(target, 3);
                Require(target.NumberOfFiles == 7, "Replace slot mapping is incorrect.");
            }

            byte[] original = File.ReadAllBytes(destination);
            using (var cancel = new CancellationTokenSource())
            {
                var cancelling = new InlineProgress(_ => cancel.Cancel());
                Throws<OperationCanceledException>(() => MirArchiveOperations.CreateAsset(source, destination,
                    "", true, null, AlphaMode.Preserve, true, cancelling, cancel.Token));
                Require(File.ReadAllBytes(destination).SequenceEqual(original), "Cancelled conversion replaced the old asset.");
            }
            string cancelledRoot = Path.Combine(root, "cancelled-export");
            using (var cancel = new CancellationTokenSource())
            {
                Throws<OperationCanceledException>(() => MirArchiveOperations.Extract(source, cancelledRoot,
                    null, AlphaMode.Preserve, new InlineProgress(_ => cancel.Cancel()), cancel.Token));
                Require(!Directory.Exists(MirArchiveOperations.ExtractionPath(source, cancelledRoot)), "Cancelled extraction was published.");
            }
            string damaged = Fixture(root, wzl, corrupt: true);
            Throws<InvalidDataException>(() => MirArchiveOperations.CreateAsset(damaged, destination, "", true,
                null, AlphaMode.Preserve, true, null, default));
            Require(File.ReadAllBytes(destination).SequenceEqual(original), "Failed conversion replaced the old asset.");
            Throws<InvalidDataException>(() => MirArchiveOperations.Extract(damaged, exportRoot, null, AlphaMode.Preserve, null, default));
            Require(!Directory.Exists(MirArchiveOperations.ExtractionPath(damaged, exportRoot)), "Failed extraction was published.");

            string missing = Path.Combine(root, "missing" + Path.GetExtension(source));
            File.Copy(source, missing);
            Throws<FileNotFoundException>(() => MirArchiveOperations.CreateAsset(missing, missing + ".asset", "", true,
                null, AlphaMode.Preserve, false, null, default));
            Require(!Directory.EnumerateFileSystemEntries(root, "*.tmp", SearchOption.AllDirectories).Any(), "Temporary outputs leaked.");
            Console.WriteLine($"PASS {(wzl ? "WZL" : "WIL")}: PNG/schema, asset round-trip, offsets, alpha, empty frames, append/overwrite/replace, cancellation and failures");
        }

        // Cross the 256-frame batch boundary and cancel after the first committed batch.
        string large = Fixture(root, true, count: 260);
        using (var target = AssetFileStream.Create(Path.Combine(root, "batches.asset"), ""))
        {
            MirArchiveOperations.ImportToAsset(large, target, null, null, default);
            Require(target.NumberOfFiles == 260, "Batching lost frame slots.");
        }
        using (var target = AssetFileStream.Create(Path.Combine(root, "partial.asset"), ""))
        using (var cancel = new CancellationTokenSource())
        {
            Throws<OperationCanceledException>(() => MirArchiveOperations.ImportToAsset(large, target, null,
                new InlineProgress(p => { if (p.Completed > 256) cancel.Cancel(); }), cancel.Token));
            Require(target.NumberOfFiles == 256, "Cancellation did not preserve exactly the completed batch.");
        }
        // Commands in the application run inside WPF's dispatcher synchronization context.
        // This console smoke host has no Application.Run loop, so install that context explicitly.
        var previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            VerifyImportModel(root, large);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        VerifyDepthsAndPalette(root);
        Console.WriteLine("PASS batch boundaries and asynchronous import command");
    }

    private static void VerifyImportModel(string root, string source)
    {
        using var target = AssetFileStream.Create(Path.Combine(root, "model.asset"), "");
        var model = new ImportDialogModel { stream = target, IsMirMode = true, ImportSource = source };
        bool closed = false;
        model.OnClose += (_, e) => closed = e.DialogResult;
        Require(model.SubmitCommand.CanExecute(null), "Import command is disabled with valid inputs.");
        model.SubmitCommand.Execute(null);
        Require(model.IsBusy && !model.CanEdit && !model.SubmitCommand.CanExecute(null), "Import did not lock its controls.");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (model.IsBusy && timer.Elapsed < TimeSpan.FromSeconds(20))
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Require(!model.IsBusy && closed && model.Progress == 100 && target.NumberOfFiles == 260, "Import command did not complete successfully.");

        var failed = new ImportDialogModel { stream = target, IsMirMode = true, ImportSource = Path.Combine(root, "absent.wil") };
        bool failedClosed = false;
        failed.OnClose += (_, _) => failedClosed = true;
        failed.SubmitCommand.Execute(null);
        WaitFor(failed);
        Require(!failedClosed && failed.StatusText.StartsWith("操作失败：") && failed.CanEdit, "Failed import was reported as success or left UI locked.");

        var cancelled = new ImportDialogModel { stream = target, IsMirMode = true, ImportSource = source };
        bool cancelledClosed = false;
        cancelled.OnClose += (_, _) => cancelledClosed = true;
        cancelled.SubmitCommand.Execute(null);
        Require(!cancelled.RequestWindowClose() && !cancelledClosed, "Window closed while the writer was active.");
        WaitFor(cancelled);
        Require(cancelledClosed, "Deferred close did not run after cancellation.");

        // The existing ordinary file-import branch must continue to accept exported PNGs.
        string png = Path.Combine(root, "export", "sample-wil-4", "0000000.png");
        var ordinary = new ImportDialogModel { stream = target };
        ordinary.SetSourceFiles([png]);
        ordinary.ImportUserData = ImageUserData.None;
        uint before = target.NumberOfFiles;
        ordinary.SubmitCommand.Execute(null);
        WaitFor(ordinary);
        Require(ordinary.Progress == 100 && target.NumberOfFiles == before + 1, "Ordinary image import regressed.");
        Require(target.Read(before)!.Data.SequenceEqual(Premultiplied), "Ordinary PNG pixels changed.");
    }

    private static void WaitFor(ResourceOperationModel model)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (model.IsBusy && timer.Elapsed < TimeSpan.FromSeconds(20))
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Require(!model.IsBusy, "Operation did not finish within 20 seconds.");
    }

    private static void VerifyDepthsAndPalette(string root)
    {
        byte[] palette = new byte[768];
        palette[3] = 100; palette[4] = 60; palette[5] = 20;
        string palettePath = Path.Combine(root, "custom.pal");
        File.WriteAllBytes(palettePath, palette);
        foreach (var (wzl, bits, alpha4) in new[] { (false, 8, false), (false, 16, false), (true, 8, false), (true, 16, false), (true, 24, false), (true, 16, true) })
        {
            string path = Path.Combine(root, $"depth-{bits}-{alpha4}{(wzl ? ".wzl" : ".wil")}");
            using (var file = File.Create(path))
            using (var writer = new BinaryWriter(file))
            {
                byte[] header = new byte[wzl ? 48 : 56];
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(44), 1);
                if (!wzl) BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(48), bits == 8 ? 256 : 65536);
                writer.Write(header);
                if (!wzl && bits == 8) writer.Write(new byte[1024]);
                uint offset = (uint)file.Position;
                if (wzl)
                {
                    writer.Write((ushort)((alpha4 ? 0x0800 : 0x0100) | (bits == 8 ? 3 : bits == 16 ? 5 : 6)));
                    writer.Write((ushort)0);
                }
                writer.Write((short)2); writer.Write((short)1); writer.Write((short)-1); writer.Write((short)2);
                if (wzl) writer.Write(0u); // Raw WZL with a compression flag: parser decides by stored length.
                writer.Write(bits switch
                {
                    8 => new byte[] { 1, 0, 0, 0 },
                    16 => new byte[] { 0, 248, 0, 0 },
                    _ => new byte[] { 0, 0, 255, 0, 0, 0, 0, 0 }
                });
                if (alpha4) writer.Write((byte)0x8F);
                byte[] index = new byte[52];
                BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(44), 1);
                BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(48), offset);
                File.WriteAllBytes(Path.ChangeExtension(path, wzl ? ".wzx" : ".wix"), index);
            }
            string destination = path + ".asset";
            MirArchiveOperations.CreateAsset(path, destination, "", true, bits == 8 ? palettePath : null,
                AlphaMode.Preserve, false, null, default);
            using (var asset = AssetFileStream.Open(destination, ""))
            {
                byte[] expected = bits == 8 ? [100, 60, 20, 255, 0, 0, 0, 0]
                    : alpha4 ? [136, 0, 0, 136, 0, 0, 0, 255] : [255, 0, 0, 255, 0, 0, 0, 0];
                Require(asset.Read(0)!.Data.SequenceEqual(expected), $"{bits}-bit {(wzl ? "WZL" : "WIL")} pixels changed.");
            }
            if (bits == 8)
            {
                MirArchiveOperations.CreateAsset(path, destination, "", true, palettePath,
                    AlphaMode.FromBlackBackground, true, null, default);
                using var asset = AssetFileStream.Open(destination, "");
                Require(asset.Read(0)!.Data[3] == 100, "Black-background alpha option was ignored.");
            }
        }
        Console.WriteLine("PASS 8/16-bit WIL, 8/16/24-bit raw WZL, alpha4, custom palette and black-background alpha");
    }

    private static void VerifyFrames(AssetFileStream asset, uint start)
    {
        var first = asset.Read(start)!;
        Require(first.Width == 2 && first.Height == 2 && first.OffsetX == -17 && first.OffsetY == 9, "Asset dimensions/offsets changed.");
        Require(first.lpType == ImageTypes.PNG && first.Data.SequenceEqual(Premultiplied), "Asset RGBA premultiplication changed.");
        Require(asset.Read(start + 1)!.Data.Length == 0 && asset.Read(start + 3)!.Data.Length == 0, "Empty slots were dropped.");
        var single = asset.Read(start + 2)!;
        Require(single.Width == 1 && single.Height == 1 && single.Data.SequenceEqual(new byte[] { 10, 20, 30, 255 }), "Valid 1x1 frame was lost.");
        Require(single.OffsetX == 2 && single.OffsetY == -3, "Single frame offset changed.");
    }

    private static (int Width, int Height, byte[] Pixels) ReadPng(string path)
    {
        using var file = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        AlphaUtil.SwitchRedBlue(bytes);
        return (bitmap.PixelWidth, bitmap.PixelHeight, bytes);
    }

    private static string Fixture(string root, bool wzl, bool corrupt = false, int count = 4)
    {
        string path = Path.Combine(root, $"sample-{(wzl ? "wzl" : "wil")}-{count}{(corrupt ? "-bad" : "")}{(wzl ? ".wzl" : ".wil")}");
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        byte[] header = new byte[wzl ? 48 : 56];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(44), count);
        if (!wzl) BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(48), -1);
        writer.Write(header);
        uint first = (uint)file.Position;
        // DIB bottom-up BGRA: blue/transparent on the bottom, red/half-transparent green on top.
        WriteFrame(writer, wzl, 2, 2, -17, 9, [255, 0, 0, 64, 0, 0, 0, 0, 0, 0, 255, 255, 0, 255, 0, 128]);
        uint second = (uint)file.Position;
        WriteFrame(writer, wzl, 1, 1, 2, -3, [30, 20, 10, 255]);
        if (corrupt) file.SetLength(file.Length - 1);
        byte[] index = new byte[48 + count * 4];
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(44), count);
        BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(48), first);
        BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(56), second);
        File.WriteAllBytes(Path.ChangeExtension(path, wzl ? ".wzx" : ".wix"), index);
        return path;
    }

    private static void WriteFrame(BinaryWriter writer, bool wzl, short width, short height, short x, short y, byte[] data)
    {
        if (wzl)
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(data);
            writer.Write((ushort)0x0107);
            writer.Write((ushort)0);
            writer.Write(width); writer.Write(height); writer.Write(x); writer.Write(y);
            writer.Write((uint)compressed.Length);
            writer.Write(compressed.ToArray());
        }
        else
        {
            writer.Write(width); writer.Write(height); writer.Write(x); writer.Write(y);
            writer.Write(data);
        }
    }

    private sealed class InlineProgress(Action<ResourceOperationProgress> action) : IProgress<ResourceOperationProgress>
    { public void Report(ResourceOperationProgress value) => action(value); }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
