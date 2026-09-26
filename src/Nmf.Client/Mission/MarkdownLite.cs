using System.Text;
using System.Text.RegularExpressions;

namespace Nmf.Client.Mission;

/// <summary>Turns the little markdown briefings use (# and ## headings, - lists, **bold**) into Godot BBCode.</summary>
public static partial class MarkdownLite
{
    public static string ToBbcode(string markdown)
    {
        var sb = new StringBuilder();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = Inline(raw.TrimEnd().Replace("[", "[lb]"));
            if (line.StartsWith("## ", StringComparison.Ordinal))
                sb.Append("[font_size=22][b]").Append(line[3..]).Append("[/b][/font_size]\n");
            else if (line.StartsWith("# ", StringComparison.Ordinal))
                sb.Append("[font_size=30][b]").Append(line[2..]).Append("[/b][/font_size]\n");
            else if (line.StartsWith("- ", StringComparison.Ordinal))
                sb.Append("  • ").Append(line[2..]).Append('\n');
            else
                sb.Append(line).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static string Inline(string line) => Bold().Replace(line, "[b]$1[/b]");

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();
}
