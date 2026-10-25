namespace Mir2ResourceParser;

internal static class ResourcePaths
{
    // Windows 客户端经常混用 .WZL/.wzl 和 .WZX/.wzx，兼容 Linux/macOS 大小写敏感目录。
    public static string Companion(string resourcePath, string extension)
    {
        string expected = Path.ChangeExtension(resourcePath, extension);
        if (File.Exists(expected)) return expected;
        string directory = Path.GetDirectoryName(Path.GetFullPath(expected))!;
        if (!Directory.Exists(directory)) return expected;
        string name = Path.GetFileName(expected);
        string[] matches = Directory.EnumerateFiles(directory)
            .Where(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length > 1) throw new InvalidDataException($"配对文件存在多个大小写不同的候选：{expected}");
        return matches.Length == 1 ? matches[0] : expected;
    }
}
