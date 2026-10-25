using System.Text;
using GameResource.Package.Internal;
using Xunit;

namespace GameResource.Package.Tests;

public sealed class XxHash64Tests
{
    [Theory]
    [InlineData("", 0xEF46DB3751D8E999UL)]
    [InlineData("a", 0xD24EC4F1A98C6E5BUL)]
    [InlineData("abc", 0x44BC2CF5AD770999UL)]
    public void MatchesKnownSeedZeroVectors(string text, ulong expected)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Assert.Equal(expected, XxHash64.HashToUInt64(bytes));
        var hash = new XxHash64();
        hash.Append(bytes);
        Assert.Equal(expected, hash.GetCurrentHashAsUInt64());
    }

    // Frozen using System.IO.Hashing 10.0.0, without referencing it from this test project.
    [Theory]
    [InlineData(4, 0xFFCED8604453CC1EUL)]
    [InlineData(8, 0x884A173614B81B8DUL)]
    [InlineData(15, 0xA948F5F0F6ABAC2DUL)]
    [InlineData(31, 0xC346D2B59B4D8EE1UL)]
    [InlineData(32, 0xCBF59C5116FF32B4UL)]
    [InlineData(33, 0x0C535D1ACAFB8EADUL)]
    [InlineData(64, 0xF7C67301DB6713F0UL)]
    [InlineData(65, 0xC31EB63B2AE4465BUL)]
    [InlineData(256, 0x1FACBE8406CD904BUL)]
    [InlineData(65536, 0x8B778F53FD34FC28UL)]
    [InlineData(65537, 0x971CD345D4EA3AA9UL)]
    public void MatchesReferenceVectorsAcrossStripeAndBlockBoundaries(int length, ulong expected)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++) data[i] = (byte)i;
        Assert.Equal(expected, XxHash64.HashToUInt64(data));
        var hash = new XxHash64();
        for (int offset = 0; offset < data.Length; offset += 7)
            hash.Append(data.AsSpan(offset, Math.Min(7, data.Length - offset)));
        Assert.Equal(expected, hash.GetCurrentHashAsUInt64());
    }

    [Fact]
    public void EverySplitAcrossStripesPreservesSnapshotsAndLaterAppends()
    {
        byte[] bytes = new byte[131];
        new Random(20261025).NextBytes(bytes);
        for (int length = 0; length <= 129; length++)
        {
            // Use an unaligned input slice; the backing array includes unrelated bytes.
            ReadOnlySpan<byte> data = bytes.AsSpan(1, length);
            ulong expected = XxHash64.HashToUInt64(data);
            for (int split = 0; split <= length; split++)
            {
                var hash = new XxHash64();
                hash.Append(data[..split]);
                ulong snapshot = XxHash64.HashToUInt64(data[..split]);
                Assert.Equal(snapshot, hash.GetCurrentHashAsUInt64());
                hash.Append([]);
                Assert.Equal(snapshot, hash.GetCurrentHashAsUInt64());
                hash.Append(data[split..]);
                Assert.Equal(expected, hash.GetCurrentHashAsUInt64());
                Assert.Equal(expected, hash.GetCurrentHashAsUInt64());
            }
        }
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(1048593)]
    public void BytewiseAndRandomChunksMatchOneShot(int length)
    {
        byte[] data = new byte[length];
        var random = new Random(length);
        random.NextBytes(data);
        ulong expected = XxHash64.HashToUInt64(data);
        var bytewise = new XxHash64();
        for (int i = 0; i < data.Length; i++) bytewise.Append(data.AsSpan(i, 1));
        Assert.Equal(expected, bytewise.GetCurrentHashAsUInt64());

        var chunked = new XxHash64();
        for (int offset = 0; offset < length;)
        {
            int count = Math.Min(random.Next(1, 8193), length - offset);
            chunked.Append(data.AsSpan(offset, count));
            offset += count;
        }
        Assert.Equal(expected, chunked.GetCurrentHashAsUInt64());
    }

    [Fact]
    public void IndependentInstancesSupportConcurrentReaders()
    {
        Parallel.For(0, 128, i =>
        {
            byte[] data = new byte[65536 + i];
            new Random(i).NextBytes(data);
            ulong expected = XxHash64.HashToUInt64(data);
            var hash = new XxHash64();
            hash.Append(data.AsSpan(0, i));
            hash.Append(data.AsSpan(i));
            Assert.Equal(expected, hash.GetCurrentHashAsUInt64());
        });
    }

    [Fact]
    public void HashIsInternalAndDoesNotReferenceSystemIoHashing()
    {
        var assembly = typeof(ResourcePackage).Assembly;
        Assert.Same(assembly, typeof(XxHash64).Assembly);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), name => name.Name == "System.IO.Hashing");
        Assert.DoesNotContain(typeof(XxHash64), assembly.GetExportedTypes());
    }
}
