using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Game.Asset.Package;
using Game.Asset.Package.Internal;

namespace Game.Asset.Cli.Sources;

internal static class PropertyMetadata
{
    private sealed record Value(AssetMetadataType Type, object Data, int Count);
    internal static byte[] Parse(string text, string source = "metadata")
    {
        var fields = new Dictionary<string, Value>(StringComparer.Ordinal);
        int lineNumber = 0;
        foreach (string raw in text.TrimStart('\uFEFF').Split('\n'))
        {
            lineNumber++; string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            try
            {
                int equal = line.IndexOf('=');
                if (equal <= 0) throw new FormatException("Expected property=value.");
                string key = line[..equal].Trim(), literal = line[(equal + 1)..].Trim();
                string? type = null; int colon = key.LastIndexOf(':');
                if (colon >= 0) { type = key[(colon + 1)..].Trim(); key = key[..colon].Trim(); }
                if (key.Length == 0 || AssetPath.Utf8.GetByteCount(key) > 1024 || key.Any(char.IsControl)) throw new FormatException("Invalid property name.");
                if (!fields.TryAdd(key, ParseValue(literal, type))) throw new FormatException($"Duplicate property '{key}'.");
            }
            catch (Exception e) when (e is FormatException or JsonException or OverflowException or InvalidOperationException)
            { throw new InvalidDataException($"{source}:{lineNumber}: {e.Message}", e); }
        }
        var sorted = fields.OrderBy(p => p.Key, AssetPath.Comparer).ToArray();
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, AssetPath.Utf8, true);
        writer.Write("AMTA"u8); writer.Write(sorted.Length); stream.SetLength(8L + sorted.Length * 24L); stream.Position = stream.Length;
        var descriptors = new byte[sorted.Length][];
        for (int i = 0; i < sorted.Length; i++)
        {
            byte[] key = AssetPath.Utf8.GetBytes(sorted[i].Key), d = new byte[24];
            BinaryPrimitives.WriteInt32LittleEndian(d, checked((int)stream.Position));
            BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(4), key.Length);
            writer.Write(key); descriptors[i] = d;
        }
        for (int i = 0; i < sorted.Length; i++)
        {
            while (stream.Position % 8 != 0) writer.Write((byte)0);
            var v = sorted[i].Value; var d = descriptors[i]; long start = stream.Position;
            WriteValue(writer, v);
            if (stream.Length > PackageHeader.MaxMetadataBytes) throw new InvalidDataException($"{source}: encoded metadata exceeds 1 MiB.");
            BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(8), checked((int)start));
            BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(12), checked((int)(stream.Position - start)));
            BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(16), v.Count); d[20] = (byte)v.Type;
        }
        stream.Position = 8; foreach (var d in descriptors) writer.Write(d);
        var result = stream.ToArray(); _ = new AssetMetadata(result); return result;
    }
    private static Value ParseValue(string text, string? type)
    {
        bool array = type?.EndsWith("[]", StringComparison.Ordinal) == true || (type is null && text.StartsWith('['));
        if (array)
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 2 });
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("Expected an array.");
            var items = doc.RootElement.EnumerateArray().ToArray();
            string element = type is not null ? type[..^2] : InferArray(items);
            return element switch
            {
                "string" => new(AssetMetadataType.StringArray, items.Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new FormatException("Expected string array.")).ToArray(), items.Length),
                "int64" => new(AssetMetadataType.Int64Array, items.Select(x => x.TryGetInt64(out long n) ? n : throw new FormatException("Expected int64 array.")).ToArray(), items.Length),
                "double" => new(AssetMetadataType.DoubleArray, items.Select(ReadDouble).ToArray(), items.Length),
                "bool" => new(AssetMetadataType.BooleanArray, items.Select(x => x.ValueKind is JsonValueKind.True or JsonValueKind.False ? x.GetBoolean() : throw new FormatException("Expected bool array.")).ToArray(), items.Length),
                _ => throw new FormatException($"Unsupported metadata type '{type}'.")
            };
        }
        if (type is null)
        {
            if (text.StartsWith('"')) type = "string";
            else if (bool.TryParse(text, out _)) type = "bool";
            else if (IsInteger(text)) type = "int64";
            else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) type = "double";
            else type = "string";
        }
        return type switch
        {
            "string" => new(AssetMetadataType.String, text.StartsWith('"') ? JsonSerializer.Deserialize<string>(text) ?? throw new FormatException("Null is not supported.") : text, 1),
            "int64" => new(AssetMetadataType.Int64, long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), 1),
            "double" => new(AssetMetadataType.Double, Finite(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)), 1),
            "bool" => new(AssetMetadataType.Boolean, bool.Parse(text), 1),
            _ => throw new FormatException($"Unsupported metadata type '{type}'.")
        };
    }
    private static bool IsInteger(string text)
    {
        var span = text.AsSpan(); if (span.Length > 0 && span[0] is '+' or '-') span = span[1..];
        return span.Length > 0 && span.IndexOfAnyExceptInRange('0', '9') < 0;
    }
    private static string InferArray(JsonElement[] items)
    {
        if (items.Length == 0) return "string";
        if (items.All(x => x.ValueKind == JsonValueKind.Number))
            return items.All(x => IsInteger(x.GetRawText())) ? "int64" : "double";
        if (items.All(x => x.ValueKind == JsonValueKind.String)) return "string";
        if (items.All(x => x.ValueKind is JsonValueKind.True or JsonValueKind.False)) return "bool";
        throw new FormatException("Arrays must contain one supported scalar type.");
    }
    private static double ReadDouble(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number) throw new FormatException("Expected double array.");
        double result = Finite(value.GetDouble());
        if (IsInteger(value.GetRawText()))
        {
            long integer = value.GetInt64();
            if ((System.Numerics.BigInteger)result != integer) throw new FormatException("Integer cannot be converted to double without loss; use int64[].");
        }
        return result;
    }
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new FormatException("NaN and infinity are not supported.");
    private static void WriteValue(BinaryWriter w, Value value)
    {
        switch (value.Type)
        {
            case AssetMetadataType.String: w.Write(AssetPath.Utf8.GetBytes((string)value.Data)); break;
            case AssetMetadataType.Int64: w.Write((long)value.Data); break;
            case AssetMetadataType.Double: w.Write((double)value.Data); break;
            case AssetMetadataType.Boolean: w.Write((bool)value.Data); break;
            case AssetMetadataType.Int64Array: foreach (long v in (long[])value.Data) w.Write(v); break;
            case AssetMetadataType.DoubleArray: foreach (double v in (double[])value.Data) w.Write(v); break;
            case AssetMetadataType.BooleanArray: foreach (bool v in (bool[])value.Data) w.Write(v); break;
            case AssetMetadataType.StringArray:
                var strings = ((string[])value.Data).Select(AssetPath.Utf8.GetBytes).ToArray();
                int offset = checked((strings.Length + 1) * 4);
                w.Write(offset); foreach (var s in strings) { offset = checked(offset + s.Length); w.Write(offset); }
                foreach (var s in strings) w.Write(s);
                break;
        }
    }
}
