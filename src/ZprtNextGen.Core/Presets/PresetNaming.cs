using System;
using System.Text.RegularExpressions;

namespace ZprtNextGen.Core.Presets;

/// <summary>
/// Идентификаторы и русские названия пресетов из имён upstream-файлов.
/// </summary>
public static class PresetNaming
{
    private static readonly Regex CollapseDashes = new("-{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Естественный порядок идентификаторов: числа сравниваются по значению,
    /// поэтому «general-alt2» идёт раньше «general-alt10», а не наоборот.
    /// </summary>
    public static IComparer<string> IdComparer { get; } = new NaturalIdComparer();

    /// <summary>
    /// Группы в фиксированном порядке: (id, отображаемое имя группы).
    /// </summary>
    internal static readonly (string Id, string Name)[] GroupsOrdered = new[]
    {
        ("general", "Обычные"),
        ("faketls", "Fake TLS"),
        ("simplefake", "Simple Fake"),
        ("experimental", "Экспериментальные"),
    };

    public static string ToId(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException("Имя файла не может быть пустым.", nameof(fileName));
        }

        var name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var lowered = name.ToLowerInvariant();
        var cleaned = lowered.Replace("(", string.Empty).Replace(")", string.Empty).Replace(" ", "-");
        var collapsed = CollapseDashes.Replace(cleaned, "-");
        var trimmed = collapsed.Trim('-');
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException($"Не удалось сформировать id из имени файла: {fileName}", nameof(fileName));
        }
        return trimmed;
    }

    public static (string Name, string Description) Describe(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("Id пресета не может быть пустым.", nameof(id));
        }

        if (id == "general")
        {
            return ("Основной", "Базовый набор: Discord, YouTube и общий список. Начни с него.");
        }

        if (id.StartsWith("general-alt", StringComparison.Ordinal))
        {
            var n = AltNumber(id, "general-alt");
            return ($"Альтернатива {n}", $"Вариант {n}: другая комбинация стратегий обхода.");
        }

        if (id == "general-exp")
        {
            return ("Экспериментальный", "Экспериментальные стратегии. Может работать нестабильно.");
        }

        if (id == "general-fake-tls-auto")
        {
            return ("Fake TLS (авто)", "Автоматический подбор fake TLS. Часто помогает на мобильных операторах.");
        }

        if (id.StartsWith("general-fake-tls-auto-alt", StringComparison.Ordinal))
        {
            var n = AltNumber(id, "general-fake-tls-auto-alt");
            return ($"Fake TLS, вариант {n}", $"Fake TLS, альтернативная комбинация {n}.");
        }

        if (id == "general-simple-fake")
        {
            return ("Simple Fake", "Упрощённый fake без сложных модификаций.");
        }

        if (id.StartsWith("general-simple-fake-alt", StringComparison.Ordinal))
        {
            var n = AltNumber(id, "general-simple-fake-alt");
            return ($"Simple Fake, вариант {n}", $"Simple Fake, альтернативная комбинация {n}.");
        }

        // Экспериментальный без номера (на случай появления новых).
        if (id.StartsWith("general-exp", StringComparison.Ordinal) && id.Length > "general-exp".Length)
        {
            return ("Экспериментальный", "Экспериментальные стратегии. Может работать нестабильно.");
        }

        return (id, "Пресет из upstream.");
    }

    internal static bool TryParseGroupId(string id, out string groupId)
    {
        groupId = "general";
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (id.StartsWith("general-fake-tls-auto", StringComparison.Ordinal)
            || id.StartsWith("general-fake-tls", StringComparison.Ordinal))
        {
            groupId = "faketls";
            return true;
        }

        if (id.StartsWith("general-simple-fake", StringComparison.Ordinal))
        {
            groupId = "simplefake";
            return true;
        }

        if (id.StartsWith("general-exp", StringComparison.Ordinal))
        {
            groupId = "experimental";
            return true;
        }

        if (id.StartsWith("general-alt", StringComparison.Ordinal))
        {
            groupId = "general";
            return true;
        }

        if (id == "general")
        {
            groupId = "general";
            return true;
        }

        return true;
    }

    internal static int AltNumber(string id, string prefix)
    {
        var rest = id[prefix.Length..];
        return rest.Length == 0 ? 1 : (int.TryParse(rest, out var n) ? n : 1);
    }

    /// <summary>Сравнить идентификаторы естественным образом (числовые части — по значению).</summary>
    public static int CompareIds(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var endX = i;
                while (endX < x.Length && char.IsDigit(x[endX]))
                    endX++;
                var endY = j;
                while (endY < y.Length && char.IsDigit(y[endY]))
                    endY++;

                // Сравниваем числовые части без переполнения: сначала по длине без ведущих нулей.
                var startX = i;
                while (startX < endX - 1 && x[startX] == '0')
                    startX++;
                var startY = j;
                while (startY < endY - 1 && y[startY] == '0')
                    startY++;

                var lengthX = endX - startX;
                var lengthY = endY - startY;
                if (lengthX != lengthY)
                    return lengthX.CompareTo(lengthY);

                for (var k = 0; k < lengthX; k++)
                {
                    var digit = x[startX + k].CompareTo(y[startY + k]);
                    if (digit != 0)
                        return digit;
                }

                i = endX;
                j = endY;
                continue;
            }

            var cx = char.ToLowerInvariant(x[i]);
            var cy = char.ToLowerInvariant(y[j]);
            if (cx != cy)
                return cx.CompareTo(cy);

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }

    private sealed class NaturalIdComparer : IComparer<string>
    {
        public int Compare(string? x, string? y) => CompareIds(x, y);
    }
}
