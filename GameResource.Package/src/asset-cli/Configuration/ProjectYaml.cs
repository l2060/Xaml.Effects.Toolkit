using System.Text;
using System.Text.Json;

namespace Game.Asset.Cli.Configuration;

/// <summary>Strict configuration-oriented YAML subset: block mappings/sequences and quoted/plain scalars.</summary>
internal static class ProjectYaml
{
    internal abstract record Node(int Line);
    internal sealed record Scalar(string Value, int SourceLine) : Node(SourceLine);
    internal sealed record Map(Dictionary<string, Node> Values, int SourceLine) : Node(SourceLine);
    internal sealed record Sequence(List<Node> Values, int SourceLine) : Node(SourceLine);
    private sealed record Line(int Indent, string Text, int Number);

    internal static Map Parse(string text)
    {
        var lines = new List<Line>(); int number = 0;
        foreach (string raw in text.TrimStart('\uFEFF').Split('\n'))
        {
            number++; if (raw.Contains('\t')) throw Error(number, "Tabs are not supported; use spaces.");
            string value = StripComment(raw.TrimEnd('\r')).TrimEnd(); if (string.IsNullOrWhiteSpace(value)) continue;
            int indent = value.Length - value.TrimStart().Length;
            if (indent % 2 != 0) throw Error(number, "Use two-space indentation.");
            lines.Add(new(indent, value[indent..], number));
        }
        int position = 0;
        if (lines.Count == 0) throw Error(1, "Empty project configuration.");
        Node root = ReadNode(0);
        if (position != lines.Count || root is not Map map) throw Error(lines[Math.Min(position, lines.Count - 1)].Number, "Expected a root mapping.");
        return map;

        Node ReadNode(int indent)
        {
            if (position >= lines.Count || lines[position].Indent != indent) throw Error(lines[Math.Min(position, lines.Count - 1)].Number, "Invalid indentation.");
            var first = lines[position];
            if (!first.Text.StartsWith('-')) { var m = new Map(new(StringComparer.Ordinal), first.Number); ReadMap(m, indent); return m; }
            var sequence = new Sequence([], first.Number);
            while (position < lines.Count && lines[position].Indent == indent && lines[position].Text.StartsWith('-'))
            {
                var line = lines[position++];
                if (!line.Text.StartsWith("- ", StringComparison.Ordinal)) throw Error(line.Number, "Expected '- value'.");
                string rest = line.Text[2..].Trim();
                if (Colon(rest) >= 0)
                {
                    var m = new Map(new(StringComparer.Ordinal), line.Number); Pair(m, rest, indent + 2, line.Number);
                    ReadMap(m, indent + 2); sequence.Values.Add(m);
                }
                else sequence.Values.Add(new Scalar(Unquote(rest, line.Number), line.Number));
            }
            return sequence;
        }
        void ReadMap(Map map, int indent)
        {
            while (position < lines.Count && lines[position].Indent == indent && !lines[position].Text.StartsWith('-'))
            { var l = lines[position++]; Pair(map, l.Text, indent, l.Number); }
        }
        void Pair(Map map, string value, int indent, int line)
        {
            int colon = Colon(value);
            if (colon <= 0) throw Error(line, "Expected key: value.");
            string key = Unquote(value[..colon].Trim(), line), rest = value[(colon + 1)..].Trim();
            Node node;
            if (rest.Length > 0) node = new Scalar(Unquote(rest, line), line);
            else
            {
                if (position >= lines.Count || lines[position].Indent != indent + 2) throw Error(line, "Expected an indented mapping or sequence.");
                node = ReadNode(indent + 2);
            }
            if (!map.Values.TryAdd(key, node)) throw Error(line, $"Duplicate key '{key}'.");
        }
    }
    private static int Colon(string text)
    {
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote == '"' && c == '\\') { i++; continue; }
            if (c == quote) quote = '\0'; else if (quote == '\0' && c is '\'' or '"') quote = c;
            else if (quote == '\0' && c == ':' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]))) return i;
        }
        return -1;
    }
    private static string StripComment(string text)
    {
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote == '"' && c == '\\') { i++; continue; }
            if (c == quote) quote = '\0'; else if (quote == '\0' && c is '\'' or '"') quote = c;
            else if (quote == '\0' && c == '#' && (i == 0 || char.IsWhiteSpace(text[i - 1]))) return text[..i];
        }
        return text;
    }
    private static string Unquote(string text, int line)
    {
        if (text.Length == 0) throw Error(line, "Empty scalar; use quoted empty strings.");
        if (text[0] == '"')
        { try { return JsonSerializer.Deserialize<string>(text) ?? throw Error(line, "Null scalar."); } catch (JsonException e) { throw Error(line, e.Message); } }
        if (text[0] == '\'')
        { if (text.Length < 2 || text[^1] != '\'') throw Error(line, "Unclosed quoted string."); return text[1..^1].Replace("''", "'"); }
        if (text[0] is '&' or '*' or '!' or '|' or '>' or '[' or '{' || text is "---" or "...")
            throw Error(line, "Unsupported YAML syntax. Use block mappings, block sequences and quoted/plain scalars.");
        return text;
    }
    internal static InvalidDataException Error(int line, string message) => new($"asset-project.yml:{line}: {message}");
}
