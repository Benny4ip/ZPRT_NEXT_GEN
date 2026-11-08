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
        return rest.Length == 0 ? 1 : int.Parse(rest);
    }
}
