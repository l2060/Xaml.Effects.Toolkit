using System.Security.Cryptography;
using GameResource.Package.Internal;
using MessagePack;
using Xunit;

namespace GameResource.Package.Tests;

[MessagePackObject]
public sealed class SpriteMetadata
{
    [Key("width")] public int Width { get; set; }
    [Key("name")] public string Name { get; set; } = "";
    [Key("tags")] public string[] Tags { get; set; } = [];
}

public sealed partial class PackageTests
{
    [Theory]
    [InlineData(false, PackageCompression.None)]
    [InlineData(false, PackageCompression.Auto)]
    [InlineData(true, PackageCompression.None)]
    [InlineData(true, PackageCompression.Auto)]
    public async Task ConcurrentSyncAndAsyncReadersHaveIndependentBuffersAndPositions(bool encrypted, PackageCompression compression)
    {
        byte[] bytes = Data(65536 * 3 + 5);
        Array.Clear(bytes, 65536, 65536);
        using (var writer = Create(encrypted))
        {
            using var input = new MemoryStream(bytes);
            writer.WriteResource("sprite", input, new SpriteMetadata { Width = 42, Name = "cat", Tags = ["monster"] }, new() { Compression = compression });
            writer.Save();
        }
        using var package = await ResourcePackage.OpenAsync(PathFor(), Options(encrypted));
        Assert.Equal(0, package.GetStatistics().MetadataCacheEntries);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            using var stream = await package.OpenResourceAsync<SpriteMetadata>("sprite");
            Assert.Equal(42, stream.Metadata!.Width);
            byte[] part = new byte[70_000];
            int offset = i * 1000;
            stream.Data.Position = offset;
            await stream.Data.ReadExactlyAsync(part);
            Assert.Equal(bytes.AsSpan(offset, part.Length).ToArray(), part);
            using var result = i % 2 == 0 ? await package.ReadResourceAsync("sprite") : package.ReadResource("sprite");
            Assert.Equal(bytes, result.Data.ToArray());
            Assert.Equal("cat", result.Metadata!.GetString("name"));
        }));
        package.Verify();
    }

    [Fact]
    public async Task MetadataMapsInteroperateWithDtosAndRetainUnknownValues()
    {
        using var package = Create();
        Put(package, "a", [1]);
        var properties = new ResourceProperties(("width", 17), ("name", "cat"), ("tags", new ResourceValue[] { "a", "b" }),
            ("extra", new ResourceProperties(("unsigned", ulong.MaxValue), ("signed", long.MinValue),
                ("blob", new byte[] { 0, 128, 255 }), ("real", 0.125), ("nil", ResourceValue.Null))));
        package.UpdateMetadata("a", properties);
        var dto = await package.ReadMetadataAsync<SpriteMetadata>("a");
        Assert.Equal(17, dto!.Width);
        Assert.Equal(new[] { "a", "b" }, dto.Tags);
        var extra = package.ReadMetadata("a")["extra"].AsMap();
        Assert.Equal(ulong.MaxValue, extra["unsigned"].AsUInt64());
        Assert.Equal(long.MinValue, extra.GetInt64("signed"));
        Assert.Equal(new byte[] { 0, 128, 255 }, extra["blob"].AsBinary().ToArray());
        Assert.Equal(0.125, extra.GetDouble("real"));
        Assert.Equal(ResourceValueKind.Null, extra["nil"].Kind);
        Assert.Throws<OverflowException>(() => extra.GetInt64("unsigned"));
        Assert.False(extra.TryGetInt32("unsigned", out _));
        package.UpdateMetadata("a", new SpriteMetadata { Width = 33, Name = "dog", Tags = ["pet"] });
        Assert.Equal(33, package.ReadMetadata("a").GetInt32("width"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataIsNotReadAtOpenLookupOrContentOnlyRead(bool encrypted)
    {
        using (var writer = Create(encrypted))
        {
            Put(writer, "a", [1, 2], properties: """{"width":42}""");
            writer.Save();
        }
        var record = ReadRecord("a", encrypted);
        FlipByte(record.PropertiesOffset + (encrypted ? 16 + 28 : 8));
        using var package = await ResourcePackage.OpenAsync(PathFor(), Options(encrypted));
        var reference = package.GetResource("a");
        Assert.True(reference.Info.HasMetadata);
        Assert.Equal(0, package.GetStatistics().MetadataCacheEntries);
        Assert.Equal(new byte[] { 1, 2 }, Read(package, "a"));
        if (encrypted) await Assert.ThrowsAnyAsync<CryptographicException>(async () => await reference.GetMetadataAsync());
        else await Assert.ThrowsAsync<InvalidDataException>(async () => await reference.GetMetadataAsync());
        Assert.ThrowsAny<Exception>(() => package.Verify());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpdatingOnePartPreservesTheOtherAndMetadataSpaceIsReused(bool encrypted)
    {
        using (var package = Create(encrypted))
        {
            Put(package, "a", Data(100), properties: """{"width":1}""");
            package.Save();
        }
        var first = ReadRecord("a", encrypted);
        using (var package = ResourcePackage.OpenEdit(PathFor(), Options(encrypted)))
        {
            using var input = new MemoryStream(Data(200));
            package.UpdateContent("a", input);
            package.Save();
        }
        var second = ReadRecord("a", encrypted);
        Assert.Equal(first.PropertiesOffset, second.PropertiesOffset);
        using (var package = ResourcePackage.OpenEdit(PathFor(), Options(encrypted)))
        {
            package.UpdateMetadata("a", new ResourceProperties(("width", 2)));
            package.Save();
        }
        var third = ReadRecord("a", encrypted);
        Assert.Equal(second.DataOffset, third.DataOffset);
        Assert.NotEqual(second.PropertiesOffset, third.PropertiesOffset);
        using (var package = ResourcePackage.OpenEdit(PathFor(), Options(encrypted)))
        {
            for (int i = 0; i < 20; i++)
            {
                package.UpdateMetadata("a", new ResourceProperties(("width", i)));
                package.Save();
            }
            Assert.True(package.GetStatistics().PackageLength < 12000);
            using var input = new MemoryStream(new byte[] { 9 });
            package.WriteResource("a", input); // Complete replacement removes metadata.
            Assert.False(package.GetInfo("a").HasMetadata);
            package.Save();
        }
        using var reopened = ResourcePackage.OpenRead(PathFor(), Options(encrypted));
        reopened.Verify();
    }

    [Fact]
    public void FailedCombinedWriteDoesNotLoseOldMetadataOrLeakNewExtents()
    {
        using (var package = Create())
        {
            Put(package, "a", [1], properties: """{"width":1}""");
            package.Save();
            using var shortInput = new MemoryStream(new byte[10]);
            Assert.Throws<EndOfStreamException>(() => package.WriteResource("a", shortInput, new ResourceProperties(("width", 2)), new() { Length = 100 }));
            using var result = package.ReadResource("a");
            Assert.Equal(1, result.Metadata!.GetInt32("width"));
            Assert.Equal(new byte[] { 1 }, result.Data.ToArray());
            Put(package, "b", [2]);
            package.Save();
        }
        using var reopened = ResourcePackage.OpenRead(PathFor());
        reopened.Verify();
    }

    [Fact]
    public async Task AbsentMetadataBufferLimitsAndStreamLifetimeAreExplicit()
    {
        using var package = ResourcePackage.Create(PathFor(), new() { MaxBufferedResourceBytes = 10 });
        Put(package, "large", Data(11));
        Assert.Empty(await package.ReadMetadataAsync("large"));
        Assert.Null(await package.ReadMetadataAsync<SpriteMetadata>("large"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await package.ReadResourceAsync("large"));
        using (var stream = await package.OpenResourceAsync("large"))
        {
            Assert.Empty(stream.Metadata!);
            Assert.Throws<InvalidOperationException>(() => package.DeleteFile("large"));
            byte[] bytes = new byte[11];
            await stream.Data.ReadExactlyAsync(bytes);
            Assert.Equal(Data(11), bytes);
        }
        Put(package, "small", []);
        var result = package.ReadResource("small");
        Assert.Empty(result.Data.ToArray());
        result.Dispose();
        result.Dispose();
        Assert.Throws<ObjectDisposedException>(() => result.Data);
    }

    [Fact]
    public void MetadataCacheIsBoundedAndInvalidatedWithoutBreakingOwnedResults()
    {
        using var package = ResourcePackage.Create(PathFor(), new() { Password = "secret", PasswordIterations = 100_000,
            MetadataCacheBytes = 100, MetadataCacheEntries = 2 });
        for (int i = 0; i < 5; i++) Put(package, "f" + i, [1], properties: """{"width":1}""");
        using var result = package.ReadResource("f0");
        for (int i = 0; i < 5; i++) Assert.Equal(1, package.ReadMetadata("f" + i).GetInt32("width"));
        var stats = package.GetStatistics();
        Assert.InRange(stats.MetadataCacheBytes, 1, 100);
        Assert.Equal(2, stats.MetadataCacheEntries);
        package.UpdateMetadata("f0", new ResourceProperties(("width", 42)));
        Assert.Equal(0, package.GetStatistics().MetadataCacheEntries);
        Assert.Equal(1, result.Metadata!.GetInt32("width"));
        using var shared = result.Share();
        Assert.Equal(1, shared.Metadata!.GetInt32("width"));
        Assert.Equal(42, package.ReadMetadata("f0").GetInt32("width"));
    }

    [Fact]
    public async Task CanceledReadsAndOpenReleaseResourcesAndCanBeRetried()
    {
        using (var writer = Create()) { Put(writer, "a", Data(200_000)); writer.Save(); }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await ResourcePackage.OpenAsync(PathFor(), cancellationToken: canceled.Token));
        using var package = await ResourcePackage.OpenAsync(PathFor(), new() { Access = PackageAccess.ReadWrite });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await package.ReadResourceAsync("a", canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await package.OpenResourceAsync("a", canceled.Token));
        using (var stream = package.OpenRead("a"))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { _ = await stream.ReadAsync(new byte[100], canceled.Token); });
            Assert.Equal(0, stream.Position);
            byte[] part = new byte[100];
            await stream.ReadExactlyAsync(part);
            Assert.Equal(Data(200_000)[..100], part);
        }
        package.UpdateMetadata("a", new ResourceProperties(("ok", true)));
        package.Save();
    }

    [Fact]
    public void InvalidMetadataContractsAreRejectedBeforeChangingResources()
    {
        using var package = Create();
        Put(package, "a", [1]);
        Assert.Throws<InvalidDataException>(() => package.UpdateMetadata("a", new[] { 1, 2 }));
        Assert.Throws<InvalidDataException>(() => package.UpdateMetadata("a", new Dictionary<int, int> { [1] = 2 }));
        Assert.Throws<ArgumentException>(() => package.UpdateMetadata("a", new ResourceProperties(("large", new byte[1024 * 1024]))));
        var nested = ResourceProperties.Empty;
        for (int i = 0; i < 70; i++) nested = new(("child", nested));
        Assert.Throws<InvalidDataException>(() => package.UpdateMetadata("a", nested));
        Assert.False(package.GetInfo("a").HasMetadata);
        package.UpdateMetadata("a", new ResourceProperties(("width", "wrong type")));
        Assert.Throws<InvalidDataException>(() => package.ReadMetadata<SpriteMetadata>("a"));
        Assert.Equal("wrong type", package.ReadMetadata("a").GetString("width"));
    }

    [Fact]
    public async Task ActiveWriterExcludesReadersAndOtherWriters()
    {
        using var package = Create();
        Put(package, "a", [1]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var input = new PausedInput(started, release);
        var writing = Task.Run(() => package.UpdateContent("a", input));
        try
        {
            await started.Task;
            Assert.Throws<InvalidOperationException>(() => package.Exists("a"));
            Assert.Throws<InvalidOperationException>(() => package.OpenRead("a"));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await package.ReadResourceAsync("a"));
            Assert.Throws<InvalidOperationException>(() => package.RemoveMetadata("a"));
            Assert.Throws<InvalidOperationException>(() => package.Dispose());
        }
        finally { release.Set(); await writing; }
        Assert.Equal(new byte[] { 2 }, Read(package, "a"));
    }

    private sealed class PausedInput(TaskCompletionSource started, ManualResetEventSlim release) : MemoryStream(new byte[] { 2 })
    {
        public override int Read(Span<byte> buffer)
        {
            started.TrySetResult();
            release.Wait();
            return base.Read(buffer);
        }
    }

    [Fact]
    public async Task BufferedReadDetectsPlainContentCorruptionAndReleasesItsLease()
    {
        using (var writer = Create()) { Put(writer, "a", Data(500), PackageCompression.None); writer.Save(); }
        var record = ReadRecord("a");
        FlipByte(record.DataOffset + 20);
        using var package = ResourcePackage.OpenEdit(PathFor());
        await Assert.ThrowsAsync<InvalidDataException>(async () => await package.ReadResourceAsync("a"));
        Put(package, "a", [9], PackageCompression.None);
        using var corrected = package.ReadResource("a");
        Assert.Equal(new byte[] { 9 }, corrected.Data.ToArray());
    }
}
