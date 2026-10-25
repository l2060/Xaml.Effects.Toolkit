using System.Text;

namespace GameResource.Package.Internal;

internal static class PackagePath
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal const int MaxBytes = 4096;

    internal static string Normalize(string path, bool allowRoot = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.StartsWith("package://", StringComparison.Ordinal)) path = path[10..];
        path = path.TrimEnd('/');
        if (path.Length == 0)
        {
            if (allowRoot) return "";
            throw new ArgumentException("The package root is not a file or an empty-directory record.", nameof(path));
        }
        if (path[0] == '/' || path.Contains('\\') || path.Contains('\0'))
            throw new ArgumentException("Use a relative package path separated by '/'.", nameof(path));
        foreach (var segment in path.AsSpan().Split('/'))
        {
            var name = path.AsSpan()[segment];
            if (name.IsEmpty || name.SequenceEqual(".") || name.SequenceEqual(".."))
                throw new ArgumentException("Empty, '.' and '..' path segments are not allowed.", nameof(path));
            foreach (char c in name)
                if (char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|')
                    throw new ArgumentException("The package path contains an invalid character.", nameof(path));
        }
        if (Utf8.GetByteCount(path) > MaxBytes)
            throw new ArgumentException($"A package path cannot exceed {MaxBytes} UTF-8 bytes.", nameof(path));
        return path;
    }

    internal static string Parent(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    // Scalar order is also UTF-8 byte order, including non-BMP characters.
    internal static int Compare(string a, string b)
    {
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            int ca = char.ConvertToUtf32(a, x), cb = char.ConvertToUtf32(b, y);
            if (ca != cb) return ca.CompareTo(cb);
            x += ca > 0xffff ? 2 : 1;
            y += cb > 0xffff ? 2 : 1;
        }
        return (a.Length - x).CompareTo(b.Length - y);
    }
}
