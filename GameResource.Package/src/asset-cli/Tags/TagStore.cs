using System.Text.Json;
using Game.Asset.Package.Internal;

namespace Game.Asset.Cli.Tags;

internal sealed record FileState(string Package, string Path, long Length, string ContentHash, string? MetadataHash, string Encoding);
internal sealed record BuildTag(Guid TagId, DateTimeOffset BuildTimeUtc, Guid? BaseTag, string ConfigHash, string[] Outputs, int ContentChanges, int MetadataChanges);

internal static class TagStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static BuildTag Load(string state, Guid id)
    {
        string path = Path.Combine(state, "tags", id.ToString("N"), "tag.json");
        if (!File.Exists(path)) throw new FileNotFoundException($"Build Tag '{id:N}' was not found in .asset/tags.", path);
        var tag = JsonSerializer.Deserialize<BuildTag>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid Tag record.");
        if (tag.TagId != id) throw new InvalidDataException("Tag identifier does not match its directory.");
        return tag;
    }
    internal static IEnumerable<BuildTag> List(string state)
    {
        string root = Path.Combine(state, "tags"); if (!Directory.Exists(root)) yield break;
        foreach (string path in Directory.EnumerateDirectories(root))
            if (Guid.TryParseExact(Path.GetFileName(path), "N", out var id)) yield return Load(state, id);
    }
    internal static Dictionary<(string Package, string Path), FileState> LoadFiles(string state, Guid id)
    {
        _ = Load(state, id);
        using var stream = File.OpenRead(Path.Combine(state, "tags", id.ToString("N"), "files.idx"));
        using var reader = new BinaryReader(stream, AssetPath.Utf8);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual("ASTAG001"u8)) throw new InvalidDataException("Invalid Tag index.");
        long count = reader.ReadInt64(); if (count < 0 || count > (stream.Length - 16) / 20) throw new InvalidDataException("Invalid Tag record count.");
        var result = new Dictionary<(string, string), FileState>();
        for (long i = 0; i < count; i++)
        {
            string package = ReadString(reader), path = ReadString(reader); long length = reader.ReadInt64();
            string content = ReadString(reader), meta = ReadString(reader), encoding = ReadString(reader);
            AssetPath.Validate(path);
            if (length < 0 || content.Length != 64 || (meta.Length != 0 && meta.Length != 64)) throw new InvalidDataException("Invalid Tag fingerprint.");
            var f = new FileState(package, path, length, content, meta.Length == 0 ? null : meta, encoding);
            if (!result.TryAdd((package, path), f)) throw new InvalidDataException("Duplicate Tag path.");
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Tag index bytes.");
        return result;
    }
    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32(); if (length < 0 || length > 16384) throw new InvalidDataException("Invalid Tag string length.");
        byte[] b = reader.ReadBytes(length); if (b.Length != length) throw new EndOfStreamException(); return AssetPath.Utf8.GetString(b);
    }
    private static void WriteString(BinaryWriter writer, string value) { byte[] b = AssetPath.Utf8.GetBytes(value); writer.Write(b.Length); writer.Write(b); }
    internal static void Write(string directory, BuildTag tag, IEnumerable<FileState> files)
    {
        Directory.CreateDirectory(directory);
        var sorted = files.OrderBy(f => f.Package, StringComparer.Ordinal).ThenBy(f => f.Path, AssetPath.Comparer).ToArray();
        using (var stream = new FileStream(Path.Combine(directory, "files.idx"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using var w = new BinaryWriter(stream, AssetPath.Utf8, true); w.Write("ASTAG001"u8); w.Write((long)sorted.Length);
            foreach (var f in sorted)
            { WriteString(w, f.Package); WriteString(w, f.Path); w.Write(f.Length); WriteString(w, f.ContentHash); WriteString(w, f.MetadataHash ?? ""); WriteString(w, f.Encoding); }
            w.Flush(); stream.Flush(true);
        }
        using var tagFile = new FileStream(Path.Combine(directory, "tag.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(tagFile, tag, Json); tagFile.Flush(true);
    }
}
