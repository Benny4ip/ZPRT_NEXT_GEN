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

        EnsureReferencedListFiles(preset.Arguments, lists);

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

    /// <summary>
    /// Создаёт пустые файлы списков, на которые ссылается пресет через <c>{{LISTS}}</c>, если их нет.
    /// Некоторые дистрибутивы zapret ожидают наличие <c>*-user.txt</c> и т.п., иначе winws завершается с ошибкой.
    /// </summary>
    private static void EnsureReferencedListFiles(string rawArguments, string listsDirWithSeparator)
    {
        if (string.IsNullOrEmpty(rawArguments) || string.IsNullOrEmpty(listsDirWithSeparator))
            return;

        var listsDir = listsDirWithSeparator.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            Directory.CreateDirectory(listsDir);
        }
        catch
        {
            return;
        }

        // Ищем {{LISTS}}имяфайла (до пробела/кавычки/конца) в исходных аргументах.
        var idx = 0;
        while ((idx = rawArguments.IndexOf(ListsToken, idx, StringComparison.Ordinal)) >= 0)
        {
            idx += ListsToken.Length;
            var end = idx;
            while (end < rawArguments.Length)
            {
                var ch = rawArguments[end];
                if (char.IsWhiteSpace(ch) || ch == '"' || ch == '\'')
                    break;
                end++;
            }

            if (end > idx)
            {
                var name = rawArguments[idx..end];
                // Только простые имена файлов (без .. и абсолютных путей).
                if (name.IndexOf("..", StringComparison.Ordinal) < 0 &&
                    name.IndexOf(Path.DirectorySeparatorChar) < 0 &&
                    name.IndexOf(Path.AltDirectorySeparatorChar) < 0 &&
                    name.IndexOf(':') < 0)
                {
                    var full = Path.Combine(listsDir, name);
                    try
                    {
                        if (!File.Exists(full))
                            File.WriteAllText(full, string.Empty);
                    }
                    catch
                    {
                        // Лучшая попытка: отсутствие прав/блокировка не должны ломать сборку аргументов.
                    }
                }
            }
        }
    }
}
