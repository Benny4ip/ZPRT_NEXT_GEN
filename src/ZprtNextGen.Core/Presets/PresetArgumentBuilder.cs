using System;

namespace ZprtNextGen.Core.Presets;

/// <summary>
/// Собирает итоговые аргументы winws из аргументов пресета:
/// подставляет пути к папкам bin и lists движка, а также значения игрового фильтра.
/// </summary>
public static class PresetArgumentBuilder
{
    private const string BinToken = "{{BIN}}";
    private const string ListsToken = "{{LISTS}}";
    private const string GameFilterTcpToken = "{{GameFilterTCP}}";
    private const string GameFilterUdpToken = "{{GameFilterUDP}}";

    /// <summary>
    /// Разрешает плейсхолдеры в аргументах пресета относительно корня движка и выбранного режима фильтра.
    /// </summary>
    /// <param name="preset">Пресет с шаблонными аргументами.</param>
    /// <param name="engineRoot">Корень папки движка.</param>
    /// <param name="mode">Режим игрового фильтра.</param>
    /// <returns>Аргументы winws с подставленными путями и портами.</returns>
    /// <exception cref="InvalidOperationException">
    /// Аргументы пресета пусты, либо после подстановки остался неразрешённый плейсхолдер <c>{{...}}</c>.
    /// </exception>
    public static string Build(Preset preset, string engineRoot, GameFilterMode mode)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (string.IsNullOrEmpty(preset.Arguments))
        {
            throw new InvalidOperationException($"Пресет '{preset.Id}': аргументы движка пусты.");
        }

        var root = Path.GetFullPath(engineRoot);
        var bin = Path.Combine(root, "bin") + Path.DirectorySeparatorChar;
        var lists = Path.Combine(root, "lists") + Path.DirectorySeparatorChar;

        var (tcp, udp) = mode switch
        {
            GameFilterMode.Disabled => ("12", "12"),
            GameFilterMode.Tcp => ("1024-65535", "12"),
            GameFilterMode.Udp => ("12", "1024-65535"),
            GameFilterMode.All => ("1024-65535", "1024-65535"),
            _ => throw new InvalidOperationException($"Неизвестный режим игрового фильтра: {mode}"),
        };

        var result = preset.Arguments
            .Replace(BinToken, bin, StringComparison.Ordinal)
            .Replace(ListsToken, lists, StringComparison.Ordinal)
            .Replace(GameFilterTcpToken, tcp, StringComparison.Ordinal)
            .Replace(GameFilterUdpToken, udp, StringComparison.Ordinal);

        if (result.Contains("{{", StringComparison.Ordinal))
        {
            var start = result.IndexOf("{{", StringComparison.Ordinal);
            var end = result.IndexOf("}}", start, StringComparison.Ordinal);
            var token = end < 0 ? result[start..] : result[start..(end + 2)];
            throw new InvalidOperationException(
                $"Пресет '{preset.Id}': неразрешённый плейсхолдер {token}");
        }

        return result;
    }
}
