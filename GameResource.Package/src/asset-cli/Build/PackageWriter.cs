using System.Buffers;
using System.Security.Cryptography;
using System.Threading.Channels;
using Game.Asset.Cli.Sources;
using Game.Asset.Package.Internal;
using Game.Asset.Package.Internal.Lz4;

namespace Game.Asset.Cli.Build;

internal sealed class BuildEntry(SourceAsset source, bool metadataOnly)
{
    internal SourceAsset Source { get; } = source;
    internal bool MetadataOnly { get; } = metadataOnly;
    internal bool Blocked;
    internal long FirstBlock, BlockCount, MetadataOffset, DataOffset, StoredLength;
}

internal static class PackageWriter
{
    private sealed record BlockJob(BuildEntry Entry, byte[] Bytes, int Length, long Sequence);

    internal static async Task WriteAsync(string destination, string scratch, Guid tag, long buildTicks,
        BuildEntry[] entries, string? password, BuildOptions options, CancellationToken token)
    {
        Directory.CreateDirectory(scratch);
        bool encrypted = password is not null;
        long directoryLength = checked(entries.LongLength * PackageHeader.EntrySize), metaLength = 0, blockCount = 0;
        foreach (var e in entries)
        {
            directoryLength = checked(directoryLength + AssetPath.Utf8.GetByteCount(e.Source.Path));
            if (e.Source.Metadata is { } metadata) { e.MetadataOffset = metaLength; metaLength = checked(metaLength + metadata.Length); }
            e.Blocked = !e.MetadataOnly && (e.Source.Compression || encrypted);
            if (e.Blocked)
            {
                e.FirstBlock = blockCount; e.BlockCount = PackageHeader.Pages(e.Source.Length, options.BlockSize);
                blockCount = checked(blockCount + e.BlockCount);
            }
        }
        long blocksLength = checked(blockCount * PackageHeader.BlockEntrySize);
        long dataOffset = PackageHeader.Align(checked(PackageHeader.Size + PackageHeader.ProtectedLength(directoryLength) +
            PackageHeader.ProtectedLength(metaLength) + PackageHeader.ProtectedLength(blocksLength)));
        var header = new PackageHeader(tag, buildTicks, encrypted, options.BlockSize, encrypted ? 600000 : 0,
            0, entries.LongLength, directoryLength, metaLength, blocksLength, dataOffset, RandomNumberGenerator.GetBytes(16));
        byte[] key = BlockCipher.DeriveKey(header, password);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = cancellation.Token;
        string temporary = destination + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous | FileOptions.RandomAccess))
            await using (var blockTable = new FileStream(Path.Combine(scratch, "blocks.bin"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous | FileOptions.RandomAccess))
            {
                output.SetLength(dataOffset); blockTable.SetLength(blocksLength);
                long end = dataOffset;
                using var writeGate = new SemaphoreSlim(options.WriteConcurrency);
                var channel = Channel.CreateBounded<BlockJob>(new BoundedChannelOptions(options.Jobs * 2) { FullMode = BoundedChannelFullMode.Wait });

                async ValueTask WriteAt(ReadOnlyMemory<byte> bytes, long position)
                {
                    await writeGate.WaitAsync(ct).ConfigureAwait(false);
                    try { await RandomAccess.WriteAsync(output.SafeFileHandle, bytes, position, ct).ConfigureAwait(false); }
                    finally { writeGate.Release(); }
                }
                long Reserve(long size)
                {
                    // A CAS loop preserves checked 64-bit address arithmetic.
                    while (true)
                    {
                        long before = Volatile.Read(ref end), after = checked(before + size);
                        if (Interlocked.CompareExchange(ref end, after, before) == before) return before;
                    }
                }
                var workers = Enumerable.Range(0, options.Jobs).Select(_ => Task.Run(async () =>
                {
                    byte[] compressed = ArrayPool<byte>.Shared.Rent(LZ4Codec.MaximumOutputSize(options.BlockSize));
                    byte[] secured = ArrayPool<byte>.Shared.Rent(options.BlockSize + 16);
                    using var cipher = encrypted ? new BlockCipher(key, tag, 4) : null;
                    try
                    {
                        await foreach (var job in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                        {
                            try
                            {
                                byte codec = 0; ReadOnlyMemory<byte> payload = job.Bytes.AsMemory(0, job.Length);
                                if (job.Entry.Source.Compression)
                                {
                                    int size = LZ4Codec.Encode(payload.Span, compressed);
                                    if (size > 0 && size < job.Length) { codec = 1; payload = compressed.AsMemory(0, size); }
                                }
                                if (cipher is not null)
                                { cipher.Encrypt(job.Sequence, payload.Span, secured.AsSpan(0, payload.Length + 16), job.Length, codec); payload = secured.AsMemory(0, payload.Length + 16); }
                                long position = Reserve(payload.Length);
                                await WriteAt(payload, position).ConfigureAwait(false);
                                var block = new BlockRecord(position, payload.Length, job.Length, job.Sequence, codec);
                                await RandomAccess.WriteAsync(blockTable.SafeFileHandle, block.Encode(), checked(job.Sequence * 32), ct).ConfigureAwait(false);
                                Interlocked.Add(ref job.Entry.StoredLength, payload.Length);
                            }
                            finally { ArrayPool<byte>.Shared.Return(job.Bytes, encrypted); }
                        }
                    }
                    catch { cancellation.Cancel(); throw; }
                    finally { ArrayPool<byte>.Shared.Return(compressed, encrypted); ArrayPool<byte>.Shared.Return(secured, encrypted); }
                }, CancellationToken.None)).ToArray();

                async Task Produce()
                {
                    try
                    {
                        await Parallel.ForEachAsync(entries.Where(e => !e.MetadataOnly), new ParallelOptions { MaxDegreeOfParallelism = options.IoConcurrency, CancellationToken = ct }, async (e, entryToken) =>
                        {
                            await using var source = new FileStream(e.Source.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
                            if (source.Length != e.Source.Length) throw new IOException($"Source length changed: {e.Source.SourcePath}");
                            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                            if (!e.Blocked && e.Source.Length > 0) e.DataOffset = Reserve(e.Source.Length);
                            long position = 0, block = 0;
                            while (position < e.Source.Length)
                            {
                                entryToken.ThrowIfCancellationRequested();
                                int count = (int)Math.Min(options.BlockSize, e.Source.Length - position);
                                byte[]? bytes = ArrayPool<byte>.Shared.Rent(options.BlockSize);
                                try
                                {
                                    await source.ReadExactlyAsync(bytes.AsMemory(0, count), entryToken).ConfigureAwait(false);
                                    hash.AppendData(bytes.AsSpan(0, count));
                                    if (e.Blocked)
                                    { await channel.Writer.WriteAsync(new(e, bytes, count, e.FirstBlock + block), entryToken).ConfigureAwait(false); bytes = null; }
                                    else await WriteAt(bytes.AsMemory(0, count), checked(e.DataOffset + position)).ConfigureAwait(false);
                                }
                                finally { if (bytes is not null) ArrayPool<byte>.Shared.Return(bytes, encrypted); }
                                position += count; block++;
                            }
                            if (source.Length != e.Source.Length || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), e.Source.ContentHash))
                                throw new IOException($"Source content changed after scanning: {e.Source.SourcePath}");
                            if (!e.Blocked) e.StoredLength = e.Source.Length;
                        }).ConfigureAwait(false);
                        channel.Writer.TryComplete();
                    }
                    catch (Exception ex) { channel.Writer.TryComplete(ex); cancellation.Cancel(); throw; }
                }
                try { await Task.WhenAll(workers.Append(Produce())).ConfigureAwait(false); }
                finally
                {
                    cancellation.Cancel();
                    while (channel.Reader.TryRead(out var remaining)) ArrayPool<byte>.Shared.Return(remaining.Bytes, encrypted);
                }
                token.ThrowIfCancellationRequested();
                header = header with { FileLength = end };
                output.SetLength(end);

                string indexPath = Path.Combine(scratch, "directory.bin"), metaPath = Path.Combine(scratch, "metadata.bin");
                await using (var index = new FileStream(indexPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                await using (var meta = new FileStream(metaPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    long nameOffset = checked(entries.LongLength * PackageHeader.EntrySize);
                    foreach (var e in entries)
                    {
                        var record = new EntryRecord(nameOffset, AssetPath.Utf8.GetByteCount(e.Source.Path), e.MetadataOnly,
                            e.Source.Metadata is not null, e.Blocked, e.MetadataOnly ? 0 : e.Source.Length, e.DataOffset, e.StoredLength,
                            e.MetadataOffset, e.Source.Metadata?.Length ?? 0, e.FirstBlock, e.BlockCount,
                            e.MetadataOnly ? new byte[32] : e.Source.ContentHash);
                        await index.WriteAsync(record.Encode(), token).ConfigureAwait(false); nameOffset += record.PathLength;
                        if (e.Source.Metadata is { } bytes) await meta.WriteAsync(bytes, token).ConfigureAwait(false);
                    }
                    foreach (var e in entries) await index.WriteAsync(AssetPath.Utf8.GetBytes(e.Source.Path), token).ConfigureAwait(false);
                    await index.FlushAsync(token).ConfigureAwait(false); await meta.FlushAsync(token).ConfigureAwait(false);
                    await ProtectSectionAsync(index, output, header.DirectoryOffset, header, key, 1, token).ConfigureAwait(false);
                    await ProtectSectionAsync(meta, output, header.MetadataOffset, header, key, 2, token).ConfigureAwait(false);
                }
                await blockTable.FlushAsync(token).ConfigureAwait(false);
                await ProtectSectionAsync(blockTable, output, header.BlocksOffset, header, key, 3, token).ConfigureAwait(false);
                await RandomAccess.WriteAsync(output.SafeFileHandle, header.Encode(key), 0, token).ConfigureAwait(false);
                output.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    private static async Task ProtectSectionAsync(FileStream source, FileStream destination, long offset, PackageHeader header,
        byte[] key, int domain, CancellationToken token)
    {
        source.Position = 0;
        byte[] raw = ArrayPool<byte>.Shared.Rent(PackageHeader.PageSize), encoded = ArrayPool<byte>.Shared.Rent(PackageHeader.PageSize + 16);
        using var cipher = new BlockCipher(key, header.TagId, domain);
        try
        {
            long page = 0;
            while (source.Position < source.Length)
            {
                int size = (int)Math.Min(PackageHeader.PageSize, source.Length - source.Position);
                await source.ReadExactlyAsync(raw.AsMemory(0, size), token).ConfigureAwait(false);
                cipher.Encrypt(page, raw.AsSpan(0, size), encoded.AsSpan(0, size + 16), size);
                await RandomAccess.WriteAsync(destination.SafeFileHandle, encoded.AsMemory(0, size + 16), offset, token).ConfigureAwait(false);
                offset += size + 16; page++;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(raw, true); ArrayPool<byte>.Shared.Return(encoded, true); }
    }
}
