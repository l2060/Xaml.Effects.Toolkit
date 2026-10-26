using System.IO.MemoryMappedFiles;
using Microsoft.Win32.SafeHandles;

namespace Game.Asset.Package.Internal;

internal sealed class FileStorage : IDisposable
{
    private readonly FileStream _file;
    private SafeFileHandle? _async;
    private readonly MemoryMappedFile? _mapping;
    private readonly MemoryMappedViewAccessor? _view;
    internal long Length { get; }

    internal FileStorage(string path, bool mapped)
    {
        _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1, FileOptions.RandomAccess);
        try
        {
            Length = _file.Length;
            _async = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            if (mapped && Length > 0)
            {
                _mapping = MemoryMappedFile.CreateFromFile(_file, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, true);
                _view = _mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            }
        }
        catch { _mapping?.Dispose(); _async?.Dispose(); _file.Dispose(); throw; }
    }
    private SafeFileHandle AsyncHandle => _async!;
    internal unsafe void Read(Span<byte> destination, long offset)
    {
        if (!EntryRecord.Range(offset, destination.Length, Length)) throw new EndOfStreamException("Asset file is truncated.");
        if (_view is not null)
        {
            byte* pointer = null;
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try { new ReadOnlySpan<byte>(pointer + _view.PointerOffset + offset, destination.Length).CopyTo(destination); }
            finally { _view.SafeMemoryMappedViewHandle.ReleasePointer(); }
            return;
        }
        while (!destination.IsEmpty)
        {
            int n = RandomAccess.Read(_file.SafeFileHandle, destination, offset);
            if (n == 0) throw new EndOfStreamException("Asset file is truncated.");
            offset += n; destination = destination[n..];
        }
    }
    internal async ValueTask ReadAsync(Memory<byte> destination, long offset, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_view is not null) { Read(destination.Span, offset); return; }
        if (!EntryRecord.Range(offset, destination.Length, Length)) throw new EndOfStreamException("Asset file is truncated.");
        while (!destination.IsEmpty)
        {
            int n = await RandomAccess.ReadAsync(AsyncHandle, destination, offset, token).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("Asset file is truncated.");
            offset += n; destination = destination[n..];
        }
    }
    public void Dispose() { _view?.Dispose(); _mapping?.Dispose(); _async?.Dispose(); _file.Dispose(); }
}
