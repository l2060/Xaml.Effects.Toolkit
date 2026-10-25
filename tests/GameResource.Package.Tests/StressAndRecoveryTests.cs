using System.Buffers.Binary;
using System.Runtime.InteropServices;
using GameResource.Package.Internal;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace GameResource.Package.Tests;

public sealed partial class PackageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RandomEditsMatchAReferenceFileSystem(bool encrypted)
    {
        var random = new Random(20261025);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal) { "" };
        ResourcePackage package = Create(encrypted);
        void AddParents(string path)
        {
            for (string parent = PackagePath.Parent(path); parent.Length != 0; parent = PackagePath.Parent(parent))
                directories.Add(parent);
        }
        try
        {
            for (int step = 0; step < 350; step++)
            {
                string dir = $"d{random.Next(5)}/sub{random.Next(3)}";
                string path = $"{dir}/f{random.Next(12)}";
                switch (random.Next(6))
                {
                    case 0:
                    case 1:
                        byte[] data = Data(random.Next(90_000), step);
                        Put(package, path, data, random.Next(2) == 0 ? PackageCompression.Auto : PackageCompression.None);
                        files[path] = data;
                        AddParents(path);
                        break;
                    case 2:
                        Assert.Equal(files.Remove(path), package.DeleteFile(path));
                        break;
                    case 3:
                        package.CreateDirectory(dir + "/empty");
                        directories.Add(dir + "/empty");
                        AddParents(dir + "/empty");
                        break;
                    case 4:
                        string root = $"d{random.Next(5)}";
                        Assert.Equal(directories.Contains(root), package.DeleteDirectory(root, true));
                        foreach (string key in files.Keys.Where(x => x.StartsWith(root + "/", StringComparison.Ordinal)).ToArray())
                            files.Remove(key);
                        directories.RemoveWhere(x => x == root || x.StartsWith(root + "/", StringComparison.Ordinal));
                        break;
                    case 5:
                        if (files.TryGetValue(path, out byte[]? content))
                        {
                            string destination = $"{dir}/renamed{step}";
                            package.Move(path, destination);
                            files.Remove(path);
                            files.Add(destination, content);
                        }
                        break;
                }
                if (step % 13 == 0) package.Save();
                if (step % 50 == 0)
                {
                    package.Save();
                    package.Dispose();
                    package = ResourcePackage.OpenEdit(PathFor(), Options(encrypted));
                    Assert.Equal(files.Keys.Order(StringComparer.Ordinal),
                        package.EnumerateFiles().Select(x => x.Path).Order(StringComparer.Ordinal));
                    foreach (var file in files) Assert.Equal(file.Value, Read(package, file.Key));
                    foreach (string directory in directories)
                    {
                        Assert.True(package.Exists(directory));
                        string prefix = directory.Length == 0 ? "" : directory + "/";
                        string[] expected = files.Keys.Concat(directories.Where(x => x.Length != 0))
                            .Where(x => x.StartsWith(prefix, StringComparison.Ordinal) && x != directory &&
                                !x[prefix.Length..].Contains('/')).Order(StringComparer.Ordinal).ToArray();
                        Assert.Equal(expected, package.Enumerate(directory).Select(x => x.Path).Order(StringComparer.Ordinal));
                    }
                    package.Verify();
                }
            }
            package.Save();
            package.Verify();
        }
        finally { package.Dispose(); }
    }

    [Fact]
    public void ManyLongPathsUseCompactResidentIndex()
    {
        const int count = 10_000;
        long pathBytes = 0;
        using (var package = Create())
        {
            for (int i = 0; i < count; i++)
            {
                string path = $"game/monster/cat/stand/frame{i:D5}.bin";
                pathBytes += PackagePath.Utf8.GetByteCount(path);
                Put(package, path, []);
            }
            package.Save();
        }
        using var reader = ResourcePackage.OpenRead(PathFor());
        Assert.Equal(count, reader.GetStatistics().FileCount);
        Assert.Equal(64L * count + pathBytes, reader.GetStatistics().IndexMemoryBytes);
        Assert.Equal("game/monster", Assert.Single(reader.Enumerate("game")).Path);
        Assert.True(reader.Exists("game/monster/cat/stand/frame05000.bin"));
        Assert.False(reader.Exists("game/monster/cat/stand/Frame05000.bin"));
    }

    [Fact]
    public void ReusingFreeSpaceBeforeAnAbandonedSaveDoesNotCorruptCommittedFiles()
    {
        using (var package = Create())
        {
            Put(package, "deleted", Data(300_000, 1), PackageCompression.None);
            Put(package, "keep", Data(100_000, 2), PackageCompression.None);
            package.Save();
            package.DeleteFile("deleted");
            package.Save();
        }
        using (var package = ResourcePackage.OpenEdit(PathFor()))
        {
            Put(package, "uncommitted", Data(250_000, 3), PackageCompression.None);
            package.Move("keep", "renamed");
        }
        using var recovered = ResourcePackage.OpenRead(PathFor());
        Assert.Equal(Data(100_000, 2), Read(recovered, "keep"));
        Assert.False(recovered.Exists("uncommitted"));
        Assert.False(recovered.Exists("renamed"));
        recovered.Verify();
    }

    [Fact]
    public void CorruptedNewIndexFallsBackBeforeOldSpaceHasBeenReused()
    {
        using (var package = Create())
        {
            Put(package, "file", [1, 2, 3]);
            package.Save();
            Put(package, "file", Data(1000));
            package.CommitObserver = stage =>
            {
                if (stage == CommitStage.FirstHeaderFlushed) throw new IOException("Crash before mirroring.");
            };
            Assert.Throws<IOException>(() => package.Save());
        }
        PackageHeader newest;
        using (var file = File.OpenRead(PathFor()))
        {
            byte[] bytes = new byte[192];
            file.ReadExactly(bytes);
            var a = PackageHeader.Decode(bytes, file.Length)!;
            file.Position = PackageHeader.SlotSize;
            file.ReadExactly(bytes);
            var b = PackageHeader.Decode(bytes, file.Length)!;
            newest = a.Generation > b.Generation ? a : b;
        }
        FlipByte(newest.IndexOffset + CompactIndex.HeaderSize);
        using var recovered = ResourcePackage.OpenEdit(PathFor());
        Assert.Equal(new byte[] { 1, 2, 3 }, Read(recovered, "file"));
        recovered.Verify();
    }

    [Fact]
    public void FailedIndexWriteCanBeRetriedWithoutLosingStagedChanges()
    {
        using var package = Create();
        Put(package, "file", Data(80_000));
        package.CommitObserver = stage =>
        {
            if (stage == CommitStage.DataFlushed) throw new IOException("Before publication.");
        };
        Assert.Throws<IOException>(() => package.Save());
        package.CommitObserver = null;
        package.Save();
        Assert.Equal(Data(80_000), Read(package, "file"));
        package.Verify();
    }

    [Fact]
    public void LowIndexLimitDoesNotRollBackANewerCommit()
    {
        using (var package = Create())
        {
            Put(package, "old", [1]);
            package.Save();
            Put(package, "new", [2]);
            package.CommitObserver = stage =>
            {
                if (stage == CommitStage.FirstHeaderFlushed) throw new IOException("Before mirroring.");
            };
            Assert.Throws<IOException>(() => package.Save());
        }
        Assert.Throws<InvalidOperationException>(() => ResourcePackage.OpenEdit(PathFor(), new() { MaxEntries = 1 }));
        using var reader = ResourcePackage.OpenRead(PathFor());
        Assert.Equal(new byte[] { 2 }, Read(reader, "new"));
        reader.Verify();
    }

    [Fact]
    public void PropertiesCanBeCopiedReplacedAndRemovedAcrossMultipleSaves()
    {
        using var package = Create(true);
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            large = new string('x', 150_000),
            custom = new object?[] { 1, true, null }
        });
        Put(package, "file", [1], properties: json);
        package.Save();
        Put(package, "file", [2]); // null means preserve, including lazily stored properties
        package.Save();
        Assert.Equal(new string('x', 150_000), package.ReadMetadata("file").GetString("large"));
        Assert.Equal(3, package.ReadMetadata("file")["custom"].AsArray().Count);
        package.UpdateMetadata("file", new ResourceProperties(("new", 42)));
        Assert.Equal(42, package.ReadMetadata("file").GetInt32("new"));
        package.Save();
        package.RemoveMetadata("file");
        package.Save();
        Assert.Empty(package.ReadMetadata("file"));
        Assert.False(package.GetEntry("file").HasProperties);
        package.Verify();
    }

    [Fact]
    public void CompactRefusesCorruptSourceAndKeepsOriginal()
    {
        using (var package = Create()) { Put(package, "file", Data(70_000), PackageCompression.None); package.Save(); }
        var record = ReadRecord("file");
        FlipByte(record.DataOffset);
        byte[] before = File.ReadAllBytes(PathFor());
        Assert.Throws<InvalidDataException>(() => ResourcePackage.Compact(PathFor()));
        Assert.Equal(before, File.ReadAllBytes(PathFor()));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void EncryptedBlocksCannotBeSwappedBetweenResources()
    {
        using (var package = Create(true))
        {
            Put(package, "first", Data(1024, 1), PackageCompression.None);
            Put(package, "second", Data(1024, 2), PackageCompression.None);
            package.Save();
        }
        var first = ReadRecord("first", true);
        var second = ReadRecord("second", true);
        using (var file = new FileStream(PathFor(), FileMode.Open, FileAccess.ReadWrite))
        {
            byte[] payload = new byte[(int)first.StoredLength - 16];
            file.Position = first.DataOffset + 16;
            file.ReadExactly(payload);
            file.Position = second.DataOffset + 16;
            file.Write(payload);
        }
        using var reader = ResourcePackage.OpenRead(PathFor(), Options(true));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => Read(reader, "second"));
    }

    [Fact]
    public void ReadsAndEditsSparsePackageBeyondFourGiB()
    {
        const long fileOffset = (1L << 32) + PackageHeader.DataStart;
        byte[] data = Data(4096);
        using (var file = new FileStream(PathFor(), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            if (OperatingSystem.IsWindows())
                Assert.True(DeviceIoControl(file.SafeFileHandle, 0x000900c4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero));
            var blob = new Blob(fileOffset, data.Length, data.Length, data.Length, false, PackageCompression.None);
            using var output = new BlobWriteStream(file.SafeFileHandle, blob, null, BlobContext.File);
            output.Write(data);
            output.Complete();
            byte[] name = "large/file.bin"u8.ToArray();
            var record = new EntryRecord
            {
                NameLength = name.Length, RawLength = data.Length, StoredLength = data.Length,
                AllocatedLength = data.Length, DataOffset = fileOffset, ContentHash = output.ContentHash
            };
            long indexOffset = fileOffset + data.Length;
            int indexLength = CompactIndex.HeaderSize + CompactIndex.RecordSize + name.Length + 16;
            long allocated = ExtentAllocator.Align(indexLength);
            var indexBlob = new Blob(indexOffset, indexLength, indexLength, allocated, false, PackageCompression.None);
            using var index = new BlobWriteStream(file.SafeFileHandle, indexBlob, null, BlobContext.Index);
            byte[] bytes = new byte[64];
            "GRINDEX2"u8.CopyTo(bytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), name.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
            index.Write(bytes.AsSpan(0, 24));
            record.Write(bytes);
            index.Write(bytes);
            index.Write(name);
            BinaryPrimitives.WriteInt64LittleEndian(bytes, PackageHeader.DataStart);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), fileOffset - PackageHeader.DataStart);
            index.Write(bytes.AsSpan(0, 16));
            index.Complete();
            var header = new PackageHeader(1, indexOffset, indexLength, indexLength, indexOffset + allocated,
                new byte[16], 0, index.Sha256!, allocated, false);
            file.SetLength(header.CommittedLength);
            RandomAccess.Write(file.SafeFileHandle, header.Encode(null), 0);
            RandomAccess.Write(file.SafeFileHandle, header.Encode(null), PackageHeader.SlotSize);
            file.Flush(true);
        }
        using (var reader = ResourcePackage.OpenRead(PathFor()))
        {
            Assert.Equal(data, Read(reader, "large/file.bin"));
            reader.Verify();
        }
        using (var editor = ResourcePackage.OpenEdit(PathFor()))
        {
            Put(editor, "reused/free-space", [7, 8, 9]);
            editor.Save();
        }
        using var reopened = ResourcePackage.OpenRead(PathFor());
        Assert.Equal(data, Read(reopened, "large/file.bin"));
        Assert.Equal(new byte[] { 7, 8, 9 }, Read(reopened, "reused/free-space"));
        reopened.Verify();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint bytesReturned, IntPtr overlapped);
}
