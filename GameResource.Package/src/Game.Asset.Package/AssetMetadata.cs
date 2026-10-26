using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Game.Asset.Package.Internal;

namespace Game.Asset.Package;

public enum AssetMetadataType : byte { String = 1, Int64, Double, Boolean, StringArray, Int64Array, DoubleArray, BooleanArray }

/// <summary>Immutable typed metadata; values remain valid after the package set is closed.</summary>
public sealed class AssetMetadata
{
    private readonly byte[] _bytes;
    private readonly object?[] _decoded;
    public static AssetMetadata Empty { get; } = new("AMTA\0\0\0\0"u8.ToArray());
    public int Count => _decoded.Length;
    internal int ByteLength => _bytes.Length;
    internal AssetMetadata(byte[] bytes)
    {
        if (bytes.Length < 8 || bytes.Length > PackageHeader.MaxMetadataBytes || !bytes.AsSpan(0, 4).SequenceEqual("AMTA"u8))
            throw new InvalidDataException("Invalid metadata header.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        if (count < 0 || count > (bytes.Length - 8) / 24) throw new InvalidDataException("Invalid metadata field count.");
        _bytes = bytes; _decoded = new object?[count];
        ReadOnlySpan<byte> previous = [];
        int expectedKeyOffset = 8 + count * 24;
        for (int i = 0; i < count; i++)
        {
            var d = Descriptor(i);
            int ko = I32(d), kl = I32(d[4..]), vo = I32(d[8..]), vl = I32(d[12..]), n = I32(d[16..]);
            if (ko != expectedKeyOffset || kl <= 0 || kl > 1024 || !EntryRecord.Range(ko, kl, bytes.Length) ||
                vo < 8 + count * 24 || !EntryRecord.Range(vo, vl, bytes.Length) || n < 0 || d[21..].IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidDataException("Invalid metadata field range.");
            var key = bytes.AsSpan(ko, kl);
            AssetPath.Utf8.GetCharCount(key);
            if (i > 0 && previous.SequenceCompareTo(key) >= 0) throw new InvalidDataException("Metadata keys are not unique and sorted.");
            previous = key;
            expectedKeyOffset += kl;
            var value = bytes.AsSpan(vo, vl);
            var type = (AssetMetadataType)d[20];
            switch (type)
            {
                case AssetMetadataType.String: if (n != 1) throw Invalid(); AssetPath.Utf8.GetCharCount(value); break;
                case AssetMetadataType.Int64: case AssetMetadataType.Double:
                    if (n != 1 || vl != 8 || vo % 8 != 0) throw Invalid();
                    if (type == AssetMetadataType.Double && !double.IsFinite(BinaryPrimitives.ReadDoubleLittleEndian(value))) throw Invalid();
                    break;
                case AssetMetadataType.Boolean: if (n != 1 || vl != 1 || value[0] > 1) throw Invalid(); break;
                case AssetMetadataType.Int64Array: case AssetMetadataType.DoubleArray:
                    if ((long)n * 8 != vl || vo % 8 != 0) throw Invalid();
                    if (type == AssetMetadataType.DoubleArray)
                        for (int j = 0; j < n; j++) if (!double.IsFinite(BinaryPrimitives.ReadDoubleLittleEndian(value[(j * 8)..]))) throw Invalid();
                    break;
                case AssetMetadataType.BooleanArray:
                    if (n != vl) throw Invalid();
                    foreach (byte b in value) if (b > 1) throw Invalid();
                    break;
                case AssetMetadataType.StringArray:
                    if (((long)n + 1) * 4 > vl) throw Invalid();
                    int end = checked((n + 1) * 4);
                    for (int j = 0; j < n; j++)
                    {
                        int start = I32(value[(j * 4)..]), next = I32(value[((j + 1) * 4)..]);
                        if (start != end || next < start || next > vl) throw Invalid();
                        AssetPath.Utf8.GetCharCount(value[start..next]); end = next;
                    }
                    if (I32(value[(n * 4)..]) != vl || end != vl) throw Invalid();
                    break;
                default: throw Invalid();
            }
        }
        long expectedValueOffset = PackageHeader.Align(expectedKeyOffset);
        for (int i = 0; i < count; i++)
        {
            var d = Descriptor(i); int offset = I32(d[8..]), length = I32(d[12..]);
            if (offset != expectedValueOffset) throw Invalid();
            expectedValueOffset = i + 1 == count ? (long)offset + length : PackageHeader.Align((long)offset + length);
        }
        if (expectedValueOffset != bytes.Length) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("Invalid typed metadata value.");
    private static int I32(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadInt32LittleEndian(b);
    private ReadOnlySpan<byte> Descriptor(int i) => _bytes.AsSpan(8 + i * 24, 24);
    private ReadOnlySpan<byte> Value(int i) { var d = Descriptor(i); return _bytes.AsSpan(I32(d[8..]), I32(d[12..])); }
    private int Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        int size = AssetPath.Utf8.GetByteCount(key);
        if (size > 1024) return -1;
        Span<byte> name = stackalloc byte[size]; AssetPath.Utf8.GetBytes(key, name);
        int lo = 0, hi = Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2; var d = Descriptor(mid);
            int cmp = _bytes.AsSpan(I32(d), I32(d[4..])).SequenceCompareTo(name);
            if (cmp == 0) return mid;
            if (cmp < 0) lo = mid + 1; else hi = mid;
        }
        return -1;
    }
    private int Require(string key, AssetMetadataType type)
    {
        int i = Find(key);
        if (i < 0) throw new KeyNotFoundException($"Metadata property '{key}' was not found.");
        if (Descriptor(i)[20] != (byte)type) throw new InvalidCastException($"Metadata property '{key}' is not {type}.");
        return i;
    }
    public bool Contains(string key) => Find(key) >= 0;
    public AssetMetadataType GetValueType(string key)
    { int i = Find(key); return i < 0 ? throw new KeyNotFoundException(key) : (AssetMetadataType)Descriptor(i)[20]; }
    public IEnumerable<string> Keys
    {
        get { for (int i = 0; i < Count; i++) yield return KeyAt(i); }
    }
    private string KeyAt(int i) { var d = Descriptor(i); return AssetPath.Utf8.GetString(_bytes.AsSpan(I32(d), I32(d[4..]))); }
    private string StringAt(int i)
    {
        if (Volatile.Read(ref _decoded[i]) is string text) return text;
        var result = AssetPath.Utf8.GetString(Value(i));
        return (string)(Interlocked.CompareExchange(ref _decoded[i], result, null) ?? result);
    }
    public string GetString(string key) => StringAt(Require(key, AssetMetadataType.String));
    public long GetInt64(string key) => BinaryPrimitives.ReadInt64LittleEndian(Value(Require(key, AssetMetadataType.Int64)));
    public double GetDouble(string key) => BinaryPrimitives.ReadDoubleLittleEndian(Value(Require(key, AssetMetadataType.Double)));
    public bool GetBoolean(string key) => Value(Require(key, AssetMetadataType.Boolean))[0] != 0;
    public ReadOnlySpan<long> GetInt64Array(string key) => MemoryMarshal.Cast<byte, long>(Value(Require(key, AssetMetadataType.Int64Array)));
    public ReadOnlySpan<double> GetDoubleArray(string key) => MemoryMarshal.Cast<byte, double>(Value(Require(key, AssetMetadataType.DoubleArray)));
    public ReadOnlySpan<bool> GetBooleanArray(string key) => MemoryMarshal.Cast<byte, bool>(Value(Require(key, AssetMetadataType.BooleanArray)));
    public IReadOnlyList<string> GetStringArray(string key)
    {
        int i = Require(key, AssetMetadataType.StringArray);
        if (Volatile.Read(ref _decoded[i]) is ReadOnlyCollection<string> found) return found;
        var v = Value(i); int n = I32(Descriptor(i)[16..]); string[] result = new string[n];
        for (int j = 0; j < n; j++) { int a = I32(v[(j * 4)..]), b = I32(v[((j + 1) * 4)..]); result[j] = AssetPath.Utf8.GetString(v[a..b]); }
        var readOnly = Array.AsReadOnly(result);
        return (ReadOnlyCollection<string>)(Interlocked.CompareExchange(ref _decoded[i], readOnly, null) ?? readOnly);
    }
    private bool Is(string key, AssetMetadataType type, out int index) { index = Find(key); return index >= 0 && Descriptor(index)[20] == (byte)type; }
    public bool TryGetString(string key, out string? value) { bool ok = Is(key, AssetMetadataType.String, out int i); value = ok ? StringAt(i) : null; return ok; }
    public bool TryGetInt64(string key, out long value) { bool ok = Is(key, AssetMetadataType.Int64, out int i); value = ok ? BinaryPrimitives.ReadInt64LittleEndian(Value(i)) : default; return ok; }
    public bool TryGetDouble(string key, out double value) { bool ok = Is(key, AssetMetadataType.Double, out int i); value = ok ? BinaryPrimitives.ReadDoubleLittleEndian(Value(i)) : default; return ok; }
    public bool TryGetBoolean(string key, out bool value) { bool ok = Is(key, AssetMetadataType.Boolean, out int i); value = ok && Value(i)[0] != 0; return ok; }
    public bool TryGetInt64Array(string key, out ReadOnlySpan<long> value)
    { bool ok = Is(key, AssetMetadataType.Int64Array, out int i); value = ok ? MemoryMarshal.Cast<byte, long>(Value(i)) : default; return ok; }
    public bool TryGetDoubleArray(string key, out ReadOnlySpan<double> value)
    { bool ok = Is(key, AssetMetadataType.DoubleArray, out int i); value = ok ? MemoryMarshal.Cast<byte, double>(Value(i)) : default; return ok; }
    public bool TryGetBooleanArray(string key, out ReadOnlySpan<bool> value)
    { bool ok = Is(key, AssetMetadataType.BooleanArray, out int i); value = ok ? MemoryMarshal.Cast<byte, bool>(Value(i)) : default; return ok; }
    public bool TryGetStringArray(string key, out IReadOnlyList<string>? value)
    { bool ok = Is(key, AssetMetadataType.StringArray, out _); value = ok ? GetStringArray(key) : null; return ok; }
}
