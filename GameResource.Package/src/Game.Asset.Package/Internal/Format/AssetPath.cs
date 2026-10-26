using System.Text;

namespace Game.Asset.Package.Internal;

internal static class AssetPath
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static readonly IComparer<string> Comparer = System.Collections.Generic.Comparer<string>.Create(Compare);
    internal static string Validate(string path, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0 && allowEmpty) return path;
        if (path.Length == 0 || Utf8.GetByteCount(path) > 4096 || path.Contains('\\'))
            throw new ArgumentException("Expected a relative asset path, using '/', at most 4096 UTF-8 bytes.", nameof(path));
        foreach (var part in path.AsSpan().Split('/'))
        {
            var segment = path.AsSpan()[part];
            if (segment.IsEmpty || segment.SequenceEqual(".") || segment.SequenceEqual(".."))
                throw new ArgumentException("Empty, '.' and '..' path segments are not allowed.", nameof(path));
            foreach (char c in segment)
                if (char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|')
                    throw new ArgumentException("Invalid asset path character.", nameof(path));
        }
        return path;
    }

    // Unicode scalar order is UTF-8 byte order, including supplementary characters.
    internal static int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int a = 0, b = 0;
        while (a < x.Length && b < y.Length)
        {
            int p = char.ConvertToUtf32(x, a), q = char.ConvertToUtf32(y, b);
            if (p != q) return p.CompareTo(q);
            a += p > 0xffff ? 2 : 1; b += q > 0xffff ? 2 : 1;
        }
        return (x.Length - a).CompareTo(y.Length - b);
    }
}
