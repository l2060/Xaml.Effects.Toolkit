using System.Buffers;
using MessagePack;
using MessagePack.Formatters;

namespace GameResource.Package.Internal;

internal static class MetadataCodec
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
        .WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(64));
    internal static byte[] Encode<T>(T metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        byte[] bytes = MessagePackSerializer.Serialize(metadata, Options);
        if (bytes.Length > CompactIndex.MaxPropertiesBytes)
            throw new ArgumentException("Metadata cannot exceed 1 MiB.", nameof(metadata));
        // A common named-map wire contract lets typed DTOs and ResourceProperties interoperate.
        Validate(bytes);
        return bytes;
    }
    internal static T? Decode<T>(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
    {
        if (bytes.IsEmpty) return typeof(T) == typeof(ResourceProperties) ? (T)(object)ResourceProperties.Empty : default;
        try
        {
            var reader = new MessagePackReader(bytes) { CancellationToken = token };
            if (reader.NextMessagePackType != MessagePackType.Map)
                throw new InvalidDataException("Metadata must be a MessagePack map with string keys.");
            T value = MessagePackSerializer.Deserialize<T>(ref reader, Options);
            if (!reader.End) throw new InvalidDataException("Trailing metadata bytes.");
            return value;
        }
        catch (MessagePackSerializationException e) { throw new InvalidDataException("Metadata does not match the requested type or format.", e); }
    }
    internal static void Validate(ReadOnlyMemory<byte> bytes) => Decode<ResourceProperties>(bytes);
}

// Public so MessagePack's generated/dynamic formatters can construct the formatter.
public sealed class ResourcePropertiesFormatter : IMessagePackFormatter<ResourceProperties?>
{
    public void Serialize(ref MessagePackWriter writer, ResourceProperties? value, MessagePackSerializerOptions options)
    {
        if (value is null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(value.Count);
        foreach (var pair in value)
        {
            writer.Write(pair.Key);
            WriteValue(ref writer, pair.Value, options);
        }
    }
    private void WriteValue(ref MessagePackWriter writer, ResourceValue value, MessagePackSerializerOptions options)
    {
        switch (value.Kind)
        {
            case ResourceValueKind.Null: writer.WriteNil(); break;
            case ResourceValueKind.Boolean: writer.Write(value.AsBoolean()); break;
            case ResourceValueKind.Int64: writer.Write(value.AsInt64()); break;
            case ResourceValueKind.UInt64: writer.Write(value.AsUInt64()); break;
            case ResourceValueKind.Double: writer.Write(value.AsDouble()); break;
            case ResourceValueKind.String: writer.Write(value.AsString()); break;
            case ResourceValueKind.Binary: writer.Write(value.AsBinary().Span); break;
            case ResourceValueKind.Map: Serialize(ref writer, value.AsMap(), options); break;
            case ResourceValueKind.Extension:
                writer.WriteExtensionFormat(new ExtensionResult(value.ExtensionCode, new ReadOnlySequence<byte>(value.ExtensionData))); break;
            case ResourceValueKind.Array:
                writer.WriteArrayHeader(value.AsArray().Count);
                foreach (var item in value.AsArray()) WriteValue(ref writer, item, options);
                break;
            default: throw new InvalidDataException("Unknown metadata value.");
        }
    }
    public ResourceProperties Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        options.Security.DepthStep(ref reader);
        try
        {
            int count = reader.ReadMapHeader();
            if (count > reader.Sequence.Length / 2) throw new InvalidDataException("Invalid metadata map length.");
            var values = new Dictionary<string, ResourceValue>(count, StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                reader.CancellationToken.ThrowIfCancellationRequested();
                string key = reader.ReadString() ?? throw new InvalidDataException("Metadata keys cannot be null.");
                if (!values.TryAdd(key, ReadValue(ref reader, options)))
                    throw new InvalidDataException("Duplicate metadata key.");
            }
            return new(values);
        }
        finally { reader.Depth--; }
    }
    private ResourceValue ReadValue(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        switch (reader.NextMessagePackType)
        {
            case MessagePackType.Nil: reader.ReadNil(); return ResourceValue.Null;
            case MessagePackType.Boolean: return reader.ReadBoolean();
            case MessagePackType.Integer:
                if (reader.NextCode == MessagePackCode.UInt64) return reader.ReadUInt64();
                return reader.ReadInt64();
            case MessagePackType.Float: return reader.ReadDouble();
            case MessagePackType.String: return reader.ReadString();
            case MessagePackType.Binary: return ResourceValue.OwnedBinary(reader.ReadBytes()!.Value.ToArray());
            case MessagePackType.Map: return Deserialize(ref reader, options);
            case MessagePackType.Extension:
                var extension = reader.ReadExtensionFormat();
                return ResourceValue.Extension(extension.TypeCode, extension.Data.ToArray());
            case MessagePackType.Array:
                options.Security.DepthStep(ref reader);
                try
                {
                    int count = reader.ReadArrayHeader();
                    if (count > reader.Sequence.Length) throw new InvalidDataException("Invalid metadata array length.");
                    var values = new ResourceValue[count];
                    for (int i = 0; i < count; i++) values[i] = ReadValue(ref reader, options);
                    return ResourceValue.OwnedArray(values);
                }
                finally { reader.Depth--; }
            default: throw new InvalidDataException("Unsupported metadata value.");
        }
    }
}
