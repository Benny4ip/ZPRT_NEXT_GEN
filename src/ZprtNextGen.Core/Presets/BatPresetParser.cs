using System.Text.RegularExpressions;

namespace ZprtNextGen.Core.Presets;

public static class BatPresetParser
{
    private const string StartLineRegex = @"^\s*start\s+""[^""]*""\s+/min\s+""[^""]*winws\.exe""\s+(.*)$";
    private const string UnresolvedVariableRegex = @"%[A-Za-z_][A-Za-z0-9_]*%";
    private const string EscapeRegex = @"\^(.)";

    private static readonly Regex StartLineMatcher = new(StartLineRegex, RegexOptions.CultureInvariant);
    private static readonly Regex UnresolvedVariableMatcher = new(UnresolvedVariableRegex, RegexOptions.CultureInvariant);
    private static readonly Regex EscapeMatcher = new(EscapeRegex, RegexOptions.CultureInvariant);

    public static string ParseArguments(string batText, string presetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batText);
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);

        var normalized = batText.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var match = StartLineMatcher.Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var arguments = match.Groups[1].Value;
            var nextLineIndex = i + 1;
            while (arguments.EndsWith('^'))
            {
                arguments = arguments[..^1];
                if (nextLineIndex >= lines.Length)
                {
                    break;
                }

                var nextLine = lines[nextLineIndex].Trim();
                arguments += " " + nextLine;
                nextLineIndex++;
            }

            arguments = EscapeMatcher.Replace(arguments, "$1");
            arguments = arguments
                .Replace("%BIN%", "{{BIN}}")
                .Replace("%LISTS%", "{{LISTS}}")
                .Replace("%GameFilterTCP%", "{{GameFilterTCP}}")
                .Replace("%GameFilterUDP%", "{{GameFilterUDP}}");

            var unresolved = UnresolvedVariableMatcher.Match(arguments);
            if (unresolved.Success)
            {
                throw new InvalidDataException($"preset '{presetId}': unresolved variable {unresolved.Value}");
            }

            var result = arguments.Trim();
            if (result.Length == 0)
            {
                throw new InvalidDataException($"preset '{presetId}': winws arguments are empty.");
            }

            return result;
        }

        throw new InvalidDataException($"preset '{presetId}': no winws start line found.");
    }
}
