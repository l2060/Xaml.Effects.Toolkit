using System.Collections;
using System.Collections.ObjectModel;
using MessagePack;
using GameResource.Package.Internal;

namespace GameResource.Package;

public enum ResourceValueKind { Null, Boolean, Int64, UInt64, Double, String, Binary, Array, Map, Extension }

/// <summary>A typed metadata value. Input arrays are copied; returned views are read-only.</summary>
public readonly struct ResourceValue
{
    public ResourceValueKind Kind { get; }
    private readonly long _integer;
    private readonly double _real;
    private readonly object? _reference;
    private ResourceValue(ResourceValueKind kind, long integer = 0, double real = 0, object? reference = null)
    { Kind = kind; _integer = integer; _real = real; _reference = reference; }
    public static ResourceValue Null => default;
    public static implicit operator ResourceValue(bool value) => new(ResourceValueKind.Boolean, value ? 1 : 0);
    public static implicit operator ResourceValue(int value) => new(ResourceValueKind.Int64, value);
    public static implicit operator ResourceValue(long value) => new(ResourceValueKind.Int64, value);
    public static implicit operator ResourceValue(uint value) => new(ResourceValueKind.UInt64, value);
    public static implicit operator ResourceValue(ulong value) => new(ResourceValueKind.UInt64, unchecked((long)value));
    public static implicit operator ResourceValue(float value) => new(ResourceValueKind.Double, real: value);
    public static implicit operator ResourceValue(double value) => new(ResourceValueKind.Double, real: value);
    public static implicit operator ResourceValue(string? value) => value is null ? Null : new(ResourceValueKind.String, reference: value);
    public static implicit operator ResourceValue(byte[] value) => new(ResourceValueKind.Binary, reference: value.ToArray());
    public static implicit operator ResourceValue(ResourceValue[] value) =>
        new(ResourceValueKind.Array, reference: Array.AsReadOnly(value.ToArray()));
    public static implicit operator ResourceValue(ResourceProperties value) => new(ResourceValueKind.Map, reference: value);
    internal static ResourceValue OwnedBinary(byte[] bytes) => new(ResourceValueKind.Binary, reference: bytes);
    internal static ResourceValue OwnedArray(ResourceValue[] values) => new(ResourceValueKind.Array, reference: Array.AsReadOnly(values));
    internal static ResourceValue Extension(sbyte code, byte[] bytes) => new(ResourceValueKind.Extension, code, reference: bytes);
    internal sbyte ExtensionCode => checked((sbyte)_integer);
    internal ReadOnlyMemory<byte> ExtensionData => (byte[])_reference!;
    public bool AsBoolean() => Kind == ResourceValueKind.Boolean ? _integer != 0 : throw WrongType();
    public long AsInt64() => Kind switch
    {
        ResourceValueKind.Int64 => _integer,
        ResourceValueKind.UInt64 => checked((long)unchecked((ulong)_integer)),
        _ => throw WrongType()
    };
    public int AsInt32() => checked((int)AsInt64());
    public ulong AsUInt64() => Kind switch
    {
        ResourceValueKind.UInt64 => unchecked((ulong)_integer),
        ResourceValueKind.Int64 => checked((ulong)_integer),
        _ => throw WrongType()
    };
    public double AsDouble() => Kind switch
    {
        ResourceValueKind.Double => _real,
        ResourceValueKind.Int64 => _integer,
        ResourceValueKind.UInt64 => unchecked((ulong)_integer),
        _ => throw WrongType()
    };
    public string AsString() => Kind == ResourceValueKind.String ? (string)_reference! : throw WrongType();
    public ReadOnlyMemory<byte> AsBinary() => Kind == ResourceValueKind.Binary ? (byte[])_reference! : throw WrongType();
    public IReadOnlyList<ResourceValue> AsArray() => Kind == ResourceValueKind.Array ? (ReadOnlyCollection<ResourceValue>)_reference! : throw WrongType();
    public ResourceProperties AsMap() => Kind == ResourceValueKind.Map ? (ResourceProperties)_reference! : throw WrongType();
    private InvalidCastException WrongType() => new($"Metadata value has type {Kind}.");
}

/// <summary>An immutable, case-sensitive MessagePack map, independent of a package's lifetime.</summary>
[MessagePackFormatter(typeof(ResourcePropertiesFormatter))]
public sealed class ResourceProperties : IReadOnlyDictionary<string, ResourceValue>
{
    private readonly Dictionary<string, ResourceValue> _values;
    public static ResourceProperties Empty { get; } = new(Array.Empty<(string, ResourceValue)>());
    public ResourceProperties(params (string Key, ResourceValue Value)[] values)
    {
        _values = new(values.Length, StringComparer.Ordinal);
        foreach (var (key, value) in values) _values.Add(key, value);
    }
    internal ResourceProperties(Dictionary<string, ResourceValue> owned) => _values = owned;
    public ResourceValue this[string key] => _values[key];
    public IEnumerable<string> Keys => _values.Keys;
    public IEnumerable<ResourceValue> Values => _values.Values;
    public int Count => _values.Count;
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool TryGetValue(string key, out ResourceValue value) => _values.TryGetValue(key, out value);
    public int GetInt32(string key) => this[key].AsInt32();
    public long GetInt64(string key) => this[key].AsInt64();
    public string GetString(string key) => this[key].AsString();
    public bool GetBoolean(string key) => this[key].AsBoolean();
    public double GetDouble(string key) => this[key].AsDouble();
    public bool TryGetInt32(string key, out int value)
    {
        value = default;
        if (!_values.TryGetValue(key, out var item) || item.Kind is not (ResourceValueKind.Int64 or ResourceValueKind.UInt64)) return false;
        try { value = item.AsInt32(); return true; } catch (OverflowException) { return false; }
    }
    public bool TryGetString(string key, out string? value)
    {
        value = null;
        if (!_values.TryGetValue(key, out var item) || item.Kind != ResourceValueKind.String) return false;
        value = item.AsString(); return true;
    }
    public ResourceProperties With(string key, ResourceValue value)
    {
        var values = new Dictionary<string, ResourceValue>(_values, StringComparer.Ordinal) { [key] = value };
        return new(values);
    }
    public IEnumerator<KeyValuePair<string, ResourceValue>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
