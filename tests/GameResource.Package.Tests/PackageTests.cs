using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using GameResource.Package.Internal;
using Xunit;

namespace GameResource.Package.Tests;

public sealed partial class PackageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "game-package-tests-" + Guid.NewGuid().ToString("N"));
    private string PathFor(string name = "test.pkg") => Path.Combine(_directory, name);
    public PackageTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private ResourcePackage Create(bool encrypted = false, string name = "test.pkg") =>
        ResourcePackage.Create(PathFor(name), new() { Password = encrypted ? "测试 password" : null, PasswordIterations = 100_000 });
    private static PackageOpenOptions Options(bool encrypted) => new() { Password = encrypted ? "测试 password" : null };
    private static void Put(ResourcePackage package, string path, byte[] bytes,
        PackageCompression compression = PackageCompression.Auto, string? properties = null)
    {
        using var input = new MemoryStream(bytes, false);
        if (properties is null) package.PutFile(path, input, compression);
        else package.WriteResource(path, input, ParseProperties(properties), new() { Compression = compression });
    }
    private static ResourceProperties ParseProperties(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return ParseValue(document.RootElement).AsMap();
    }
    private static ResourceValue ParseValue(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Object => new ResourceProperties(value.EnumerateObject().Select(p => (p.Name, ParseValue(p.Value))).ToArray()),
        System.Text.Json.JsonValueKind.Array => value.EnumerateArray().Select(ParseValue).ToArray(),
        System.Text.Json.JsonValueKind.String => value.GetString(),
        System.Text.Json.JsonValueKind.Number => value.GetInt64(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        _ => ResourceValue.Null
    };
    private static byte[] Read(ResourcePackage package, string path)
    {
        using var stream = package.OpenRead(path);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
    private static byte[] Data(int count, int seed = 17)
    {
        byte[] result = new byte[count];
        new Random(seed).NextBytes(result);
        return result;
    }

    [Theory]
    [InlineData(false, PackageCompression.None)]
    [InlineData(false, PackageCompression.Auto)]
    [InlineData(true, PackageCompression.None)]
    [InlineData(true, PackageCompression.Auto)]
    public void BinaryRoundTripAndRandomReads(bool encrypted, PackageCompression compression)
    {
        byte[] bytes = Data(65536 * 4 + 117);
        Array.Clear(bytes, 65536, 65536);
        using (var package = Create(encrypted))
        {
            Put(package, "game/monster/cat/stand/0.png", bytes, compression, """{"offsetX":-16,"custom":{"enabled":true}}""");
            Put(package, "empty.bin", [], compression);
            package.Save();
        }
        using var reopened = ResourcePackage.OpenRead(PathFor(), Options(encrypted));
        Assert.Equal(bytes, Read(reopened, "package://game/monster/cat/stand/0.png"));
        Assert.Empty(Read(reopened, "empty.bin"));
        Assert.False(reopened.Exists("Game/monster/cat/stand/0.png"));
        var metadata = reopened.ReadMetadata("game/monster/cat/stand/0.png");
        Assert.Equal(-16, metadata.GetInt32("offsetX"));
        Assert.True(metadata["custom"].AsMap().GetBoolean("enabled"));
        byte[] part = new byte[65536 + 100];
        foreach (long offset in new long[] { 0, 65530, 65536 * 2 + 3, bytes.Length - 20L, bytes.Length, bytes.Length + 9L })
        {
            int n = reopened.ReadAt("game/monster/cat/stand/0.png", offset, part);
            int expected = (int)Math.Min(part.Length, Math.Max(0, bytes.Length - offset));
            Assert.Equal(expected, n);
            Assert.Equal(bytes.AsSpan((int)Math.Min(offset, bytes.Length), expected).ToArray(), part.AsSpan(0, n).ToArray());
        }
        reopened.Verify();
    }

    [Fact]
    public void EmptyDirectoryMarkersAreCanonicalAndPersist()
    {
        using (var package = Create())
        {
            package.CreateDirectory("a/b/empty");
            Assert.Equal(1, package.GetStatistics().EmptyDirectoryCount);
            Assert.Equal(PackageEntryKind.Directory, package.GetEntry("a").Kind);
            Put(package, "a/b/empty/e", [1]);
            Put(package, "a/b/empty/f", [2]);
            Assert.Equal(0, package.GetStatistics().EmptyDirectoryCount);
            Assert.Equal(2, package.GetStatistics().FileCount);
            package.DeleteFile("a/b/empty/e");
            package.DeleteFile("a/b/empty/f");
            Assert.Equal(1, package.GetStatistics().EmptyDirectoryCount);
            package.Save();
        }
        using var reopened = ResourcePackage.OpenEdit(PathFor());
        Assert.Equal("a/b/empty", Assert.Single(reopened.Enumerate("a/b")).Path);
        Assert.True(reopened.DeleteDirectory("a/b/empty"));
        Assert.True(reopened.Exists("a/b"));
        Assert.Empty(reopened.Enumerate("a/b"));
        Assert.Equal(1, reopened.GetStatistics().EmptyDirectoryCount);
        reopened.Save();
        reopened.Verify();
    }

    [Fact]
    public void PathsAreCaseSensitiveAndDirectoriesUseSlashBoundaries()
    {
        using var package = Create();
        Put(package, "a/b/e", [1]);
        Put(package, "a/b/f", [2]);
        Put(package, "a/bb/keep", [3]);
        Put(package, "a/b/F", [4]);
        Put(package, "a/b/中文😀", [5]);
        Put(package, "a/b/\uE000", [6]);
        package.CreateDirectory("a/b/unused");
        Assert.Equal(6, package.Enumerate("a/b").Count());
        Assert.Equal(5, package.EnumerateFiles("a/b").Count());
        Assert.Equal(new byte[] { 4 }, Read(package, "package://a/b/F"));
        package.Move("a/b", "root/moved");
        Assert.False(package.Exists("a/b"));
        Assert.True(package.Exists("a/bb/keep"));
        Assert.True(package.Exists("root/moved/unused"));
        Assert.Equal(5, package.EnumerateFiles("root", true).Count());
        Assert.Empty(package.EnumerateFiles("root", false));
        package.Save();
        package.Verify();
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/../b")]
    [InlineData("a/./b")]
    [InlineData("/absolute")]
    [InlineData("a//b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a\0b")]
    public void RejectsInvalidPaths(string path)
    {
        using var package = Create();
        Assert.Throws<ArgumentException>(() => Put(package, path, [1]));
    }

    [Fact]
    public void FileDirectoryConflictsAndFailedMovesLeaveIndexUnchanged()
    {
        using var package = Create();
        Put(package, "a/b", [1]);
        Assert.Throws<IOException>(() => Put(package, "a/b/c", [2]));
        Assert.Throws<IOException>(() => Put(package, "a", [2]));
        Assert.Throws<IOException>(() => package.CreateDirectory("a/b"));
        Assert.Throws<IOException>(() => package.Move("a", "a/nested"));
        Assert.Throws<IOException>(() => package.DeleteDirectory("a"));
        Assert.Throws<IOException>(() => package.DeleteFile("a"));
        Assert.Throws<ArgumentException>(() => package.Move("a", new string('x', 4096)));
        Assert.Equal(new byte[] { 1 }, Read(package, "a/b"));
        package.Save();
    }

    [Fact]
    public void ReadersCoexistAndExcludeWrites()
    {
        using var package = Create();
        Put(package, "a", [1, 2, 3]);
        using (var stream = package.OpenRead("a"))
        {
            Assert.Throws<InvalidOperationException>(() => package.Save());
            Assert.True(package.Exists("a"));
            using var second = package.OpenRead("a");
            Assert.Equal(1, second.ReadByte());
            Assert.Throws<InvalidOperationException>(() => package.Dispose());
            Assert.Equal(1, stream.ReadByte());
        }
        using (var entries = package.Enumerate().GetEnumerator())
        {
            Assert.True(entries.MoveNext());
            Assert.Equal(new byte[] { 1, 2, 3 }, Read(package, entries.Current.Path));
            Assert.Throws<InvalidOperationException>(() => package.DeleteFile("a"));
        }
        Assert.True(package.DeleteFile("a"));
        package.Save();
    }

    [Fact]
    public void ReadOnlyAndExternalSharingRulesAreEnforced()
    {
        using (var package = Create()) { Put(package, "a", [1]); package.Save(); }
        using (var reader = ResourcePackage.OpenRead(PathFor()))
        {
            using var otherReader = ResourcePackage.OpenRead(PathFor());
            Assert.Throws<InvalidOperationException>(() => reader.CreateDirectory("dir"));
            Assert.ThrowsAny<IOException>(() => ResourcePackage.OpenEdit(PathFor()));
            Assert.Equal(new byte[] { 1 }, Read(otherReader, "a"));
        }
        using var writer = ResourcePackage.OpenEdit(PathFor());
        Assert.ThrowsAny<IOException>(() => ResourcePackage.OpenRead(PathFor()));
    }

    [Fact]
    public void DeleteAndReplaceReuseSpaceWithoutOverwritingHeaders()
    {
        byte[] data = Data(1024 * 1024);
        using (var package = Create())
        {
            Put(package, "one", data, PackageCompression.None);
            Put(package, "two", data, PackageCompression.None);
            package.Save();
            long initial = package.GetStatistics().PackageLength;
            package.DeleteFile("one");
            Assert.True(package.GetStatistics().PendingReleaseBytes >= data.Length);
            package.Save();
            Assert.True(package.GetStatistics().FreeBytes >= data.Length);
            Put(package, "replacement", data, PackageCompression.None);
            package.Save();
            Assert.True(package.GetStatistics().PackageLength < initial + 4096);
            for (int i = 0; i < 8; i++)
            {
                Put(package, "replacement", data.AsSpan(0, 120_000 + i).ToArray(), PackageCompression.None);
                package.Save();
            }
            package.Verify();
        }
        using var reopened = ResourcePackage.OpenRead(PathFor());
        Assert.Equal(data, Read(reopened, "two"));
        Assert.Equal(data.AsSpan(0, 120_007).ToArray(), Read(reopened, "replacement"));
    }

    [Fact]
    public void DisposeDiscardsUncommittedIndexChanges()
    {
        using (var package = Create())
        {
            Put(package, "old", [1, 2, 3]);
            package.Save();
            package.DeleteFile("old");
            Put(package, "new", Data(100_000));
        }
        using var reopened = ResourcePackage.OpenEdit(PathFor());
        Assert.Equal(new byte[] { 1, 2, 3 }, Read(reopened, "old"));
        Assert.False(reopened.Exists("new"));
        Assert.Equal(reopened.GetStatistics().PackageLength, new FileInfo(PathFor()).Length);
        reopened.Verify();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropertiesUpdateAndMoveDoNotRewriteContent(bool encrypted)
    {
        using (var package = Create(encrypted))
        {
            Put(package, "a/b", Data(100_000), properties: """{"unknown":{"array":[1,2,3]}}""");
            package.Save();
        }
        var before = ReadRecord("a/b", encrypted);
        using (var package = ResourcePackage.OpenEdit(PathFor(), Options(encrypted)))
        {
            package.UpdateMetadata("a/b", ParseProperties("""{"width":42,"unknown":{"array":[1,2,3]}}"""));
            package.Move("a/b", "different/File");
            package.Save();
        }
        var after = ReadRecord("different/File", encrypted);
        Assert.Equal(before.DataOffset, after.DataOffset);
        Assert.Equal(before.ContentHash, after.ContentHash);
        using var reader = ResourcePackage.OpenRead(PathFor(), Options(encrypted));
        Assert.True(reader.Exists("a"));
        Assert.Equal(42, reader.ReadMetadata("different/File").GetInt32("width"));
        Assert.Equal(new[] { 1, 2, 3 }, reader.ReadMetadata("different/File")["unknown"].AsMap()["array"].AsArray().Select(x => x.AsInt32()));
        reader.Verify();
    }

    [Fact]
    public void PasswordAndEncryptedMetadata()
    {
        const string secret = "secret-directory/hidden-filename.txt";
        using (var package = Create(true))
        {
            Put(package, secret, Encoding.UTF8.GetBytes("Secret resource contents!"), properties: """{"secret":"property-value"}""");
            package.Save();
        }
        Assert.Throws<CryptographicException>(() => ResourcePackage.OpenRead(PathFor()));
        Assert.Throws<CryptographicException>(() => ResourcePackage.OpenRead(PathFor(), new() { Password = "wrong" }));
        byte[] file = File.ReadAllBytes(PathFor());
        Assert.Equal(-1, file.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)));
        Assert.Equal(-1, file.AsSpan().IndexOf("property-value"u8));
        using var reader = ResourcePackage.OpenRead(PathFor(), Options(true));
        Assert.Equal("Secret resource contents!", Encoding.UTF8.GetString(Read(reader, secret)));
        reader.Verify();
    }

    [Fact]
    public void AutoCompressionStoresActualCodecForEachBlock()
    {
        byte[] bytes = new byte[65536 * 2];
        Data(65536).CopyTo(bytes, 65536);
        using (var package = Create()) { Put(package, "mixed", bytes); package.Save(); }
        var record = ReadRecord("mixed");
        using var file = File.OpenRead(PathFor());
        byte[] table = new byte[32];
        file.Position = record.DataOffset;
        file.ReadExactly(table);
        Assert.Equal(1, table[12]);
        Assert.Equal(0, table[28]);
        Assert.True(record.StoredLength < bytes.Length);
        using var reader = ResourcePackage.OpenRead(PathFor());
        Assert.Equal(bytes, Read(reader, "mixed"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecompressionAndCompactionPreserveFilesPropertiesAndEmptyDirectories(bool encrypted)
    {
        byte[] repeated = new byte[512 * 1024];
        using (var package = Create(encrypted))
        {
            Put(package, "delete-me", Data(2 * 1024 * 1024), PackageCompression.None);
            Put(package, "keep", repeated, PackageCompression.None, """{"offset":-8}""");
            package.CreateDirectory("empty/leaf");
            package.Save();
            package.DeleteFile("delete-me");
            package.Recompress("keep", PackageCompression.Auto);
            package.Save();
        }
        long before = new FileInfo(PathFor()).Length;
        ResourcePackage.Compact(PathFor(), Options(encrypted));
        long after = new FileInfo(PathFor()).Length;
        Assert.True(after < before / 4, $"Before={before}, after={after}");
        using var reader = ResourcePackage.OpenRead(PathFor(), Options(encrypted));
        Assert.Equal(repeated, Read(reader, "keep"));
        Assert.True(reader.Exists("empty/leaf"));
        Assert.Equal(-8, reader.ReadMetadata("keep").GetInt32("offset"));
        reader.Verify();
    }

    [Fact]
    public void FailedAndCanceledInputPreserveOldContent()
    {
        using var package = Create();
        Put(package, "file", [1, 2, 3]);
        package.Save();
        using var tooShort = new MemoryStream(new byte[10]);
        Assert.Throws<EndOfStreamException>(() => package.PutFile("file", tooShort, length: 100_000));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var data = new MemoryStream(new byte[100]);
        Assert.Throws<OperationCanceledException>(() => package.PutFile("file", data, cancellationToken: canceled.Token));
        Assert.Equal(new byte[] { 1, 2, 3 }, Read(package, "file"));
        Put(package, "another", Data(80_000));
        package.Save();
        package.Verify();
    }

    [Fact]
    public void UnknownLengthAndShortReadsAreHandled()
    {
        byte[] data = Data(150_000);
        using var package = Create();
        using var source = new NonSeekableShortReadStream(data);
        package.PutFile("streamed", source);
        Assert.Equal(data, Read(package, "streamed"));
        package.Save();
        package.Verify();
    }

    [Theory]
    [InlineData((int)CommitStage.DataFlushed, false)]
    [InlineData((int)CommitStage.FirstHeaderFlushed, true)]
    [InlineData((int)CommitStage.BothHeadersFlushed, true)]
    public void InterruptedCommitRecoversACompleteVersion(int stageValue, bool committed)
    {
        var stage = (CommitStage)stageValue;
        using (var package = Create())
        {
            Put(package, "file", Data(100_000, 1));
            package.Save();
            Put(package, "file", Data(110_000, 2));
            package.CommitObserver = current => { if (current == stage) throw new IOException("Simulated crash."); };
            Assert.Throws<IOException>(() => package.Save());
        }
        using (var recovered = ResourcePackage.OpenEdit(PathFor()))
        {
            Assert.Equal(Data(committed ? 110_000 : 100_000, committed ? 2 : 1), Read(recovered, "file"));
            recovered.Verify();
            // After recovery, old roots must be consolidated before free-space reuse.
            Put(recovered, "subsequent", Data(95_000, 3), PackageCompression.None);
            recovered.Save();
        }
        using var final = ResourcePackage.OpenRead(PathFor());
        final.Verify();
    }

    [Fact]
    public void DamagedHeaderUsesTheOtherCopy()
    {
        using (var package = Create()) { Put(package, "a", [1, 2]); package.Save(); }
        using (var file = new FileStream(PathFor(), FileMode.Open, FileAccess.Write))
        {
            file.Position = 24;
            file.WriteByte(0xff);
        }
        using var reopened = ResourcePackage.OpenEdit(PathFor());
        Assert.Equal(new byte[] { 1, 2 }, Read(reopened, "a"));
        reopened.Verify();
    }

    [Fact]
    public void CorruptedDirectoryIndexIsRejected()
    {
        using (var package = Create()) { Put(package, "a", [1]); package.Save(); }
        var header = ReadHeader();
        FlipByte(header.IndexOffset + CompactIndex.HeaderSize);
        Assert.Throws<InvalidDataException>(() => ResourcePackage.OpenRead(PathFor()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorruptContentIsDetected(bool encrypted)
    {
        using (var package = Create(encrypted)) { Put(package, "a", Data(90_000), PackageCompression.None); package.Save(); }
        var record = ReadRecord("a", encrypted);
        long offset = record.DataOffset + (encrypted ? record.Blob.BlockCount * 16 + 40 : 30);
        FlipByte(offset);
        using var reader = ResourcePackage.OpenRead(PathFor(), Options(encrypted));
        if (encrypted) Assert.ThrowsAny<CryptographicException>(() => Read(reader, "a"));
        else Assert.Throws<InvalidDataException>(() => reader.Verify());
    }

    [Fact]
    public void TruncatedPackageAndIndexLimitsAreRejected()
    {
        using (var package = Create())
        {
            for (int i = 0; i < 10; i++) Put(package, "a/" + i, [1]);
            package.Save();
        }
        Assert.Throws<InvalidOperationException>(() => ResourcePackage.OpenRead(PathFor(), new() { MaxEntries = 3 }));
        using (var file = new FileStream(PathFor(), FileMode.Open, FileAccess.Write)) file.SetLength(file.Length - 10);
        Assert.Throws<InvalidDataException>(() => ResourcePackage.OpenRead(PathFor()));
    }

    private PackageHeader ReadHeader()
    {
        byte[] bytes = new byte[192];
        using var file = File.OpenRead(PathFor());
        file.ReadExactly(bytes);
        return PackageHeader.Decode(bytes, file.Length)!;
    }
    private EntryRecord ReadRecord(string path, bool encrypted = false)
    {
        var header = ReadHeader();
        byte[]? key = encrypted ? PackageHeader.DeriveKey("测试 password", header.Salt, header.Iterations) : null;
        using var aes = key is null ? null : new AesGcm(key, 16);
        using var file = File.OpenRead(PathFor());
        using var reader = new BlobReadStream(file.SafeFileHandle, header.IndexBlob, aes, BlobContext.Index);
        var (index, _) = CompactIndex.Load(reader, header, new());
        if (key is not null) CryptographicOperations.ZeroMemory(key);
        return index.Entries[index.Find(path)];
    }
    private void FlipByte(long offset)
    {
        using var file = new FileStream(PathFor(), FileMode.Open, FileAccess.ReadWrite);
        file.Position = offset;
        int current = file.ReadByte();
        file.Position = offset;
        file.WriteByte((byte)(current ^ 0x80));
    }

    private sealed class NonSeekableShortReadStream(byte[] bytes) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int length = Math.Min(Math.Min(buffer.Length, 137), bytes.Length - _position);
            bytes.AsSpan(_position, length).CopyTo(buffer);
            _position += length;
            return length;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
