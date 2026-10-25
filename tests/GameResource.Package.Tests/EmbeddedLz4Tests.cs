using GameResource.Package.Internal.Lz4;
using Xunit;

namespace GameResource.Package.Tests;

public sealed class EmbeddedLz4Tests
{
    [Fact]
    public void PackageContainsCodecAndLicensesWithoutExternalLz4AssemblyReference()
    {
        var assembly = typeof(ResourcePackage).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), name => name.Name == "K4os.Compression.LZ4");
        Assert.DoesNotContain(assembly.GetExportedTypes(), type => type.Namespace?.Contains("Lz4", StringComparison.Ordinal) == true);
        Assert.Same(assembly, typeof(LZ4Codec).Assembly);
        foreach (string license in new[] { "LICENSE.K4os.txt", "LICENSE.LZ4.txt" })
        {
            string resourceName = Assert.Single(assembly.GetManifestResourceNames(), name => name.EndsWith(license, StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resourceName)!);
            Assert.Contains("Copyright", reader.ReadToEnd());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(131089)]
    public void EmbeddedCodecHandlesBoundariesAndSlicedBuffers(int length)
    {
        byte[] raw = new byte[length];
        new Random(411 + length).NextBytes(raw);
        Array.Clear(raw, length / 3, length / 3);
        byte[] encoded = new byte[LZ4Codec.MaximumOutputSize(length) + 32];
        encoded.AsSpan().Fill(0xAA);
        int count = LZ4Codec.Encode(raw, encoded.AsSpan(16, encoded.Length - 32));
        Assert.True(count > 0);
        Assert.All(encoded[..16], value => Assert.Equal(0xAA, value));
        Assert.All(encoded[^16..], value => Assert.Equal(0xAA, value));
        byte[] decoded = new byte[length + 32];
        decoded.AsSpan().Fill(0xBB);
        Assert.Equal(length, LZ4Codec.Decode(encoded.AsSpan(16, count), decoded.AsSpan(16, length)));
        Assert.Equal(raw, decoded.AsSpan(16, length).ToArray());
        Assert.All(decoded[..16], value => Assert.Equal(0xBB, value));
        Assert.All(decoded[^16..], value => Assert.Equal(0xBB, value));
        Assert.True(LZ4Codec.Decode(encoded.AsSpan(16, count), new byte[length - 1]) < 0);
    }

    [Fact]
    public void TruncatedLiteralBlockIsRejected()
    {
        // Token declares fifteen literals plus an extension byte, but there are no literal bytes.
        Assert.True(LZ4Codec.Decode(new byte[] { 0xF0, 0x00 }, new byte[65536]) < 0);
    }
}
