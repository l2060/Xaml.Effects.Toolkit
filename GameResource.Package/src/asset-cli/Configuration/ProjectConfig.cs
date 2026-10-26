using System.Globalization;
using Game.Asset.Package.Internal;
using static Game.Asset.Cli.Configuration.ProjectYaml;

namespace Game.Asset.Cli.Configuration;

internal sealed record SourceConfig(string Directory, string Prefix, string[] Include, string[] Exclude);
internal sealed record PackageConfig(string Name, bool Compression, SourceConfig[] Sources);
internal sealed record ProjectConfig(string FilePath, string Root, string Output, bool Encrypted, string? KeyEnvironment, PackageConfig[] Packages)
{
    internal string StateDirectory => Path.Combine(Root, ".asset");
    internal string? Password => Encrypted ? Environment.GetEnvironmentVariable(KeyEnvironment!) is { Length: > 0 } p ? p :
        throw new InvalidOperationException($"Encryption requires environment variable '{KeyEnvironment}'.") : null;

    internal static ProjectConfig Load(string path)
    {
        path = Path.GetFullPath(path); string root = Path.GetDirectoryName(path)!;
        var map = ProjectYaml.Parse(File.ReadAllText(path, AssetPath.Utf8));
        Check(map, "version", "output", "compression", "encryption", "packages");
        if (Text(map, "version", "1") != "1") throw Error(map.Line, "Unsupported project version.");
        string output = Path.GetFullPath(Text(map, "output", "./dist"), root);
        string state = Path.Combine(root, ".asset");
        if (Within(output, state) || Within(root, output)) throw Error(map.Line, "Output must not contain the project root or be inside .asset.");
        bool compression = Bool(map, "compression", true), encrypted = false; string? keyEnvironment = null;
        if (map.Values.TryGetValue("encryption", out var encNode))
        {
            var enc = AsMap(encNode); Check(enc, "enabled", "key-environment");
            encrypted = Bool(enc, "enabled", false); keyEnvironment = Text(enc, "key-environment", "GAME_ASSET_KEY");
        }
        if (!map.Values.TryGetValue("packages", out var packageNode) || packageNode is not Sequence sequence || sequence.Values.Count == 0)
            throw Error(map.Line, "At least one package is required.");
        var packages = new List<PackageConfig>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in sequence.Values)
        {
            var p = AsMap(node); Check(p, "name", "compression", "sources");
            string name = Text(p, "name");
            if (name.Length is 0 or > 80 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) || !names.Add(name))
                throw Error(p.Line, "Package names must be unique ASCII letters, numbers, '-' or '_'.");
            if (!p.Values.TryGetValue("sources", out var s) || s is not Sequence sources || sources.Values.Count == 0) throw Error(p.Line, "Package sources are required.");
            var inputs = new List<SourceConfig>();
            foreach (var input in sources.Values)
            {
                var m = AsMap(input); Check(m, "directory", "prefix", "include", "exclude");
                string directory = Path.GetFullPath(Text(m, "directory"), root), prefix = Text(m, "prefix", "");
                AssetPath.Validate(prefix, true);
                if (!System.IO.Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw Error(m.Line, "Symbolic-link source roots are not supported.");
                if (Within(directory, state) || Within(directory, output)) throw Error(m.Line, "Source directory cannot be inside output or .asset.");
                inputs.Add(new(directory, prefix, Strings(m, "include"), Strings(m, "exclude")));
            }
            packages.Add(new(name, Bool(p, "compression", compression), inputs.ToArray()));
        }
        return new(path, root, output, encrypted, keyEnvironment, packages.ToArray());
    }
    internal static bool Within(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
    private static Map AsMap(Node node) => node as Map ?? throw Error(node.Line, "Expected mapping.");
    private static void Check(Map map, params string[] known)
    { foreach (var key in map.Values.Keys) if (!known.Contains(key, StringComparer.Ordinal)) throw Error(map.Line, $"Unknown setting '{key}'."); }
    private static string Text(Map map, string key, string? fallback = null) => map.Values.TryGetValue(key, out var n)
        ? n is Scalar s ? s.Value : throw Error(n.Line, $"'{key}' must be a scalar.") : fallback ?? throw Error(map.Line, $"Missing '{key}'.");
    private static bool Bool(Map map, string key, bool fallback)
    { string s = Text(map, key, fallback ? "true" : "false"); return s switch { "true" => true, "false" => false, _ => throw Error(map.Line, $"'{key}' must be true or false.") }; }
    private static string[] Strings(Map map, string key) => !map.Values.TryGetValue(key, out var n) ? [] :
        n is Sequence s ? s.Values.Select(v => v is Scalar text ? text.Value : throw Error(v.Line, "Expected string.")).ToArray() : throw Error(n.Line, $"'{key}' must be a block sequence.");
}
