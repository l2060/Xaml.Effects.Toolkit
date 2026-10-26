using System.Text;
using System.Text.RegularExpressions;
using Game.Asset.Cli.Configuration;

namespace Game.Asset.Cli.Sources;

internal sealed class Glob
{
    private readonly Regex _regex;
    internal Glob(string pattern, bool basename = false, bool subtree = false)
    {
        if (pattern.Length > 4096) throw new InvalidDataException("Ignore pattern is too long.");
        var b = new StringBuilder(basename ? "(?:^|/)" : "^");
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                i++;
                if (i + 1 < pattern.Length && pattern[i + 1] == '/') { i++; b.Append("(?:.*/)?"); }
                else b.Append(".*");
            }
            else if (c == '*') b.Append("[^/]*");
            else if (c == '?') b.Append("[^/]");
            else b.Append(Regex.Escape(c.ToString()));
        }
        b.Append(subtree ? "(?:/.*)?$" : "$");
        _regex = new Regex(b.ToString(), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }
    internal bool Matches(string path) => _regex.IsMatch(path);
}

internal sealed record IgnoreRule(string Root, Glob Pattern, bool Include, bool DirectoryOnly)
{
    internal static IReadOnlyList<IgnoreRule> Load(string file, string root)
    {
        if (!File.Exists(file)) return [];
        var rules = new List<IgnoreRule>();
        foreach (string raw in File.ReadLines(file))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#')) continue;
            bool include = line.StartsWith('!'); if (include) line = line[1..];
            if (line.StartsWith("\\#", StringComparison.Ordinal) || line.StartsWith("\\!", StringComparison.Ordinal)) line = line[1..];
            bool directory = line.EndsWith('/'); line = line.TrimEnd('/');
            bool anchored = line.StartsWith('/'); line = line.TrimStart('/');
            if (line.Length == 0) continue;
            rules.Add(new(root, new Glob(line, !anchored && !line.Contains('/'), subtree: true), include, directory));
        }
        return rules;
    }
    internal static bool IsIgnored(IEnumerable<IgnoreRule> rules, string fullPath, bool directory)
    {
        bool result = false;
        foreach (var rule in rules)
        {
            if (!ProjectConfig.Within(fullPath, rule.Root)) continue;
            string path = Path.GetRelativePath(rule.Root, fullPath).Replace('\\', '/');
            if ((!rule.DirectoryOnly || directory) && rule.Pattern.Matches(path)) result = !rule.Include;
        }
        return result;
    }
}
