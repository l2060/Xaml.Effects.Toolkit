using System.Buffers;
using Game.Asset.Package.Internal.Lz4;

namespace Game.Asset.Package.Internal;

internal sealed class AssetReadStream : Stream
{
    private readonly PackageReader _package;
    private readonly EntryRecord _entry;
    private IDisposable? _lease;
    private BlockCipher? _cipher;
    private byte[]? _encoded, _decoded, _plain;
    private readonly byte[] _descriptor = new byte[32];
    private long _position, _cachedBlock = -1;
    private int _busy;
    private bool _disposed;
    internal AssetReadStream(PackageReader package, EntryRecord entry, IDisposable lease)
    {
        _package = package; _entry = entry; _lease = lease;
    }
    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length { get { Check(); return _entry.Length; } }
    public override long Position { get { Check(); return _position; } set => Seek(value, SeekOrigin.Begin); }
    private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void Enter()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InvalidOperationException("Use separate streams for concurrent operations.");
        if (_disposed) { Volatile.Write(ref _busy, 0); Check(); }
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        Enter();
        try
        {
            int count = (int)Math.Min(buffer.Length, Math.Max(0, _entry.Length - _position));
            var remaining = buffer[..count];
            if (!_entry.Blocked)
            {
                if (count > 0) _package.File.Read(remaining, checked(_entry.DataOffset + _position));
                _position += count; return count;
            }
            while (!remaining.IsEmpty)
            {
                long block = _position / _package.Header.BlockSize;
                if (_cachedBlock != block) DecodeAsync(block, false, default).GetAwaiter().GetResult();
                int within = (int)(_position % _package.Header.BlockSize);
                int take = (int)Math.Min(remaining.Length, Math.Min(_package.Header.BlockSize - within, _entry.Length - _position));
                _decoded.AsSpan(within, take).CopyTo(remaining); remaining = remaining[take..]; _position += take;
            }
            return count;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = (int)Math.Min(buffer.Length, Math.Max(0, _entry.Length - _position));
            var remaining = buffer[..count];
            if (!_entry.Blocked)
            {
                if (count > 0) await _package.File.ReadAsync(remaining, checked(_entry.DataOffset + _position), cancellationToken).ConfigureAwait(false);
                _position += count; return count;
            }
            while (!remaining.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long block = _position / _package.Header.BlockSize;
                if (_cachedBlock != block) await DecodeAsync(block, true, cancellationToken).ConfigureAwait(false);
                int within = (int)(_position % _package.Header.BlockSize);
                int take = (int)Math.Min(remaining.Length, Math.Min(_package.Header.BlockSize - within, _entry.Length - _position));
                _decoded.AsMemory(within, take).CopyTo(remaining); remaining = remaining[take..]; _position += take;
            }
            return count;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }
    private async ValueTask DecodeAsync(long block, bool asynchronous, CancellationToken token)
    {
        _cachedBlock = -1;
        byte[] descriptor = _descriptor; long sequence = checked(_entry.FirstBlock + block);
        if (asynchronous) await _package.Blocks.ReadAsync(descriptor, checked(sequence * 32), token).ConfigureAwait(false);
        else _package.Blocks.Read(descriptor, checked(sequence * 32));
        int rawLength = (int)Math.Min(_package.Header.BlockSize, _entry.Length - block * _package.Header.BlockSize);
        var record = BlockRecord.Decode(descriptor, sequence, rawLength, _package.Header);
        _encoded ??= ArrayPool<byte>.Shared.Rent(_package.Header.BlockSize + 16);
        _decoded ??= ArrayPool<byte>.Shared.Rent(_package.Header.BlockSize);
        if (asynchronous) await _package.File.ReadAsync(_encoded.AsMemory(0, record.StoredLength), record.Offset, token).ConfigureAwait(false);
        else _package.File.Read(_encoded.AsSpan(0, record.StoredLength), record.Offset);
        ReadOnlySpan<byte> payload = _encoded.AsSpan(0, record.StoredLength);
        if (_package.Header.Encrypted)
        {
            if (_cipher is null || (_cipher.Group >= 0 && _cipher.Group != sequence / 1024))
            {
                if (_cipher is not null) _package.Ciphers.Return(_cipher);
                _cipher = _package.Ciphers.Rent(sequence);
            }
            _plain ??= ArrayPool<byte>.Shared.Rent(_package.Header.BlockSize);
            _cipher.Decrypt(sequence, payload, _plain.AsSpan(0, record.StoredLength - 16), rawLength, record.Codec);
            payload = _plain.AsSpan(0, record.StoredLength - 16);
        }
        if (record.Codec == 0) payload.CopyTo(_decoded);
        else if (LZ4Codec.Decode(payload, _decoded.AsSpan(0, rawLength)) != rawLength)
            throw new InvalidDataException("LZ4 output length does not match the block descriptor.");
        _cachedBlock = block;
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        Enter();
        try
        {
            long p = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(_entry.Length + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            if (p < 0) throw new IOException("Cannot seek before the asset.");
            return _position = p;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            Enter();
            try
            {
                _disposed = true;
                if (_encoded is not null) ArrayPool<byte>.Shared.Return(_encoded, _cipher is not null);
                if (_decoded is not null) ArrayPool<byte>.Shared.Return(_decoded, _cipher is not null);
                if (_plain is not null) ArrayPool<byte>.Shared.Return(_plain, true);
                if (_cipher is not null) _package.Ciphers.Return(_cipher);
                _lease?.Dispose(); _lease = null;
            }
            finally { Volatile.Write(ref _busy, 0); }
        }
        base.Dispose(disposing);
    }
    public override void Flush() => Check();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
