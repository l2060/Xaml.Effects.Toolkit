using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using GameResource.Package.Internal.Lz4;
using Microsoft.Win32.SafeHandles;

namespace GameResource.Package.Internal;

internal static class BlockAuthentication
{
    internal static void Write(Span<byte> aad, long offset, long block, int rawLength, int encodedLength,
        byte codec, BlobContext context)
    {
        aad.Clear();
        BinaryPrimitives.WriteInt64LittleEndian(aad, offset);
        BinaryPrimitives.WriteInt64LittleEndian(aad[8..], block);
        BinaryPrimitives.WriteInt32LittleEndian(aad[16..], rawLength);
        BinaryPrimitives.WriteInt32LittleEndian(aad[20..], encodedLength);
        aad[24] = codec;
        aad[25] = (byte)context;
    }
}

internal sealed class BlobReadStream : Stream
{
    private readonly SafeFileHandle _file;
    private SafeFileHandle? _asyncFile;
    private readonly Func<SafeFileHandle>? _asyncFileFactory;
    private SafeFileHandle AsyncFile => _asyncFile ??= _asyncFileFactory?.Invoke() ?? _file;
    private readonly Blob _blob;
    private readonly AesGcm? _aes;
    private readonly BlobContext _context;
    private readonly bool _ownsAes;
    private IDisposable? _lease;
    private byte[]? _encoded, _decoded, _plain;
    private byte[]? _descriptor;
    private long _position, _cachedBlock = -1;
    private int _busy;
    private bool _disposed;

    internal BlobReadStream(SafeFileHandle file, Blob blob, AesGcm? aes, BlobContext context, IDisposable? lease = null,
        bool ownsAes = false, SafeFileHandle? asynchronousFile = null, Func<SafeFileHandle>? asyncFileFactory = null)
    {
        _file = file; _blob = blob; _aes = aes; _context = context; _lease = lease; _ownsAes = ownsAes;
        _asyncFile = asynchronousFile; _asyncFileFactory = asyncFileFactory;
    }
    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length { get { Check(); return _blob.RawLength; } }
    public override long Position { get { Check(); return _position; } set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> destination)
    {
        Enter();
        try
        {
            if (_position >= _blob.RawLength || destination.IsEmpty) return 0;
            int requested = (int)Math.Min(destination.Length, _blob.RawLength - _position);
            var output = destination[..requested];
            if (!_blob.Blocked)
            {
                PackageHeader.ReadExactly(_file, output, checked(_blob.Offset + _position));
                _position += requested;
                return requested;
            }
            while (!output.IsEmpty)
            {
                long block = _position / PackageHeader.BlockSize;
                int blockPosition = (int)(_position % PackageHeader.BlockSize);
                int rawLength = (int)Math.Min(PackageHeader.BlockSize, _blob.RawLength - block * PackageHeader.BlockSize);
                int length = Math.Min(output.Length, rawLength - blockPosition);
                if (_cachedBlock != block) DecodeBlockAsync(block, rawLength, false, default).GetAwaiter().GetResult();
                _decoded!.AsSpan(blockPosition, length).CopyTo(output);
                output = output[length..];
                _position += length;
            }
            return requested;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            int requested = (int)Math.Min(destination.Length, Math.Max(0, _blob.RawLength - _position));
            var output = destination[..requested];
            if (!_blob.Blocked)
            {
                await PackageHeader.ReadExactlyAsync(AsyncFile, output, checked(_blob.Offset + _position), cancellationToken).ConfigureAwait(false);
                _position += requested;
                return requested;
            }
            while (!output.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long block = _position / PackageHeader.BlockSize;
                int blockPosition = (int)(_position % PackageHeader.BlockSize);
                int rawLength = (int)Math.Min(PackageHeader.BlockSize, _blob.RawLength - block * PackageHeader.BlockSize);
                int length = Math.Min(output.Length, rawLength - blockPosition);
                if (_cachedBlock != block) await DecodeBlockAsync(block, rawLength, true, cancellationToken).ConfigureAwait(false);
                _decoded.AsMemory(blockPosition, length).CopyTo(output);
                output = output[length..];
                _position += length;
            }
            return requested;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private async ValueTask DecodeBlockAsync(long block, int rawLength, bool asynchronous, CancellationToken token)
    {
        _cachedBlock = -1;
        _descriptor ??= new byte[24];
        var memory = _descriptor.AsMemory(0, block + 1 == _blob.BlockCount ? 16 : 24);
        if (asynchronous) await PackageHeader.ReadExactlyAsync(AsyncFile, memory, checked(_blob.Offset + block * 16), token).ConfigureAwait(false);
        else PackageHeader.ReadExactly(_file, memory.Span, checked(_blob.Offset + block * 16));
        var descriptor = memory.Span;
        long offset = BinaryPrimitives.ReadInt64LittleEndian(descriptor);
        int stored = BinaryPrimitives.ReadInt32LittleEndian(descriptor[8..]);
        byte codec = descriptor[12];
        int overhead = _aes is null ? 0 : 28;
        if (codec > 1 || (_blob.Compression == PackageCompression.None && codec != 0) ||
            descriptor[13] != 0 || descriptor[14] != 0 || descriptor[15] != 0 ||
            offset < checked(_blob.BlockCount * 16) || offset > _blob.StoredLength ||
            stored <= overhead || stored > rawLength + overhead || stored > _blob.StoredLength - offset ||
            (codec == 0 && stored != rawLength + overhead))
            throw new InvalidDataException("Invalid encoded block.");
        // Adjacent descriptors must form one continuous sequence, including the final block.
        long nextOffset;
        if (block + 1 == _blob.BlockCount) nextOffset = _blob.StoredLength;
        else nextOffset = BinaryPrimitives.ReadInt64LittleEndian(descriptor[16..]);
        if (offset + stored != nextOffset || (block == 0 && offset != _blob.BlockCount * 16))
            throw new InvalidDataException("Noncontiguous block layout.");
        _encoded ??= ArrayPool<byte>.Shared.Rent(PackageHeader.BlockSize + 28);
        _decoded ??= ArrayPool<byte>.Shared.Rent(PackageHeader.BlockSize);
        if (asynchronous) await PackageHeader.ReadExactlyAsync(AsyncFile, _encoded.AsMemory(0, stored), checked(_blob.Offset + offset), token).ConfigureAwait(false);
        else PackageHeader.ReadExactly(_file, _encoded.AsSpan(0, stored), checked(_blob.Offset + offset));
        ReadOnlySpan<byte> encoded = _encoded.AsSpan(0, stored);
        if (_aes is not null)
        {
            _plain ??= ArrayPool<byte>.Shared.Rent(PackageHeader.BlockSize);
            Span<byte> aad = stackalloc byte[32];
            BlockAuthentication.Write(aad, _blob.Offset, block, rawLength, stored - 28, codec, _context);
            _aes.Decrypt(encoded[..12], encoded[28..], encoded.Slice(12, 16), _plain.AsSpan(0, stored - 28), aad);
            encoded = _plain.AsSpan(0, stored - 28);
        }
        if (codec == 0) encoded.CopyTo(_decoded);
        else if (LZ4Codec.Decode(encoded, _decoded.AsSpan(0, rawLength)) != rawLength)
            throw new InvalidDataException("LZ4 decoded length mismatch.");
        _cachedBlock = block;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Enter();
        try
        {
            long position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(_blob.RawLength + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (position < 0) throw new IOException("Cannot seek before the resource.");
            return _position = position;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }
    private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void Enter()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("Parallel access to a resource stream is not supported.");
        if (_disposed) { Volatile.Write(ref _busy, 0); Check(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            Enter();
            try
            {
                _disposed = true;
                Return(ref _encoded); Return(ref _decoded); Return(ref _plain);
                if (_ownsAes) _aes?.Dispose();
                _lease?.Dispose(); _lease = null;
            }
            finally { Volatile.Write(ref _busy, 0); }
        }
        base.Dispose(disposing);
    }
    private void Return(ref byte[]? buffer)
    {
        if (buffer is null) return;
        ArrayPool<byte>.Shared.Return(buffer, clearArray: _aes is not null);
        buffer = null;
    }
    public override void Flush() { Check(); }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class BlobWriteStream : Stream
{
    private readonly SafeFileHandle _file;
    private readonly Blob _blob;
    private readonly AesGcm? _aes;
    private readonly BlobContext _context;
    private readonly XxHash64 _hash = new();
    private readonly IncrementalHash? _sha;
    private byte[]? _raw, _compressed, _encrypted;
    private long _position, _block, _stored;
    private int _buffered;
    private bool _completed, _disposed;

    internal BlobWriteStream(SafeFileHandle file, Blob blob, AesGcm? aes, BlobContext context)
    {
        _file = file; _blob = blob; _aes = aes; _context = context;
        _stored = blob.Blocked ? checked(blob.BlockCount * 16) : 0;
        if (context == BlobContext.Index) _sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }
    internal ulong ContentHash { get; private set; }
    internal byte[]? Sha256 { get; private set; }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed && !_completed;
    public override long Length => _position;
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed || source.Length > _blob.RawLength - _position)
            throw new InvalidOperationException("The resource exceeds its declared length.");
        _hash.Append(source);
        _sha?.AppendData(source);
        if (!_blob.Blocked)
        {
            RandomAccess.Write(_file, source, checked(_blob.Offset + _position));
            _position += source.Length;
            _stored += source.Length;
            return;
        }
        _raw ??= ArrayPool<byte>.Shared.Rent(PackageHeader.BlockSize);
        while (!source.IsEmpty)
        {
            int n = Math.Min(PackageHeader.BlockSize - _buffered, source.Length);
            source[..n].CopyTo(_raw.AsSpan(_buffered));
            source = source[n..]; _position += n; _buffered += n;
            if (_buffered == PackageHeader.BlockSize) FlushBlock();
        }
    }

    private void FlushBlock()
    {
        ReadOnlySpan<byte> payload = _raw.AsSpan(0, _buffered);
        byte codec = 0;
        if (_blob.Compression == PackageCompression.Auto)
        {
            _compressed ??= ArrayPool<byte>.Shared.Rent(LZ4Codec.MaximumOutputSize(PackageHeader.BlockSize));
            int size = LZ4Codec.Encode(payload, _compressed);
            if (size > 0 && size < payload.Length) { codec = 1; payload = _compressed.AsSpan(0, size); }
        }
        if (_aes is not null)
        {
            _encrypted ??= ArrayPool<byte>.Shared.Rent(PackageHeader.BlockSize + 28);
            var frame = _encrypted.AsSpan(0, payload.Length + 28);
            RandomNumberGenerator.Fill(frame[..12]);
            Span<byte> aad = stackalloc byte[32];
            BlockAuthentication.Write(aad, _blob.Offset, _block, _buffered, payload.Length, codec, _context);
            _aes.Encrypt(frame[..12], payload, frame[28..], frame.Slice(12, 16), aad);
            payload = frame;
        }
        Span<byte> descriptor = stackalloc byte[16];
        descriptor.Clear();
        BinaryPrimitives.WriteInt64LittleEndian(descriptor, _stored);
        BinaryPrimitives.WriteInt32LittleEndian(descriptor[8..], payload.Length);
        descriptor[12] = codec;
        RandomAccess.Write(_file, payload, checked(_blob.Offset + _stored));
        RandomAccess.Write(_file, descriptor, checked(_blob.Offset + _block * 16));
        _stored += payload.Length; _block++; _buffered = 0;
    }

    internal Blob Complete()
    {
        if (_completed || _disposed) throw new InvalidOperationException("The writer is already closed.");
        if (_position != _blob.RawLength) throw new EndOfStreamException("The input is shorter than its declared length.");
        if (_buffered != 0) FlushBlock();
        ContentHash = _hash.GetCurrentHashAsUInt64();
        Sha256 = _sha?.GetHashAndReset();
        _completed = true;
        return _blob with { StoredLength = _stored };
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            Return(ref _raw); Return(ref _compressed); Return(ref _encrypted);
            _sha?.Dispose();
        }
        base.Dispose(disposing);
    }
    private void Return(ref byte[]? buffer)
    {
        if (buffer is null) return;
        ArrayPool<byte>.Shared.Return(buffer, clearArray: _aes is not null);
        buffer = null;
    }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
