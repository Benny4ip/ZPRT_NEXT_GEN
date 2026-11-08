using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZprtNextGen.Core.Presets;

/// <summary>
/// Сканирует папку движка и формирует каталог пресетов из upstream-.bat файлов.
/// </summary>
public static class PresetImporter
{
    /// <summary>Проверка на отсутствие движка — без него работать нельзя.</summary>
    public static PresetCatalog Import(string engineRoot, string? engineVersion = null)
    {
        if (string.IsNullOrWhiteSpace(engineRoot))
        {
            throw new ArgumentException("Путь к папке движка не задан.", nameof(engineRoot));
        }

        var root = Path.GetFullPath(engineRoot);

        var winwsPath = Path.Combine(root, "bin", "winws.exe");
        var listPath = Path.Combine(root, "lists", "list-general.txt");
        var missing = new List<string>();
        if (!File.Exists(winwsPath))
        {
            missing.Add($"bin\\winws.exe ({winwsPath})");
        }
        if (!File.Exists(listPath))
        {
            missing.Add($"lists\\list-general.txt ({listPath})");
        }
        if (missing.Count > 0)
        {
            throw new InvalidDataException(
                $"Папка движка неполная. Не найдены: {string.Join(", ", missing)}. Укажите папку с zapret-discord-youtube.");
        }

        var bats = Directory.GetFiles(root, "general*.bat")
            .Where(p => !Path.GetFileName(p).Equals("service.bat", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => PresetNaming.ToId(Path.GetFileName(p)))
            .ToList();

        var presets = new List<Preset>(bats.Count);
        foreach (var bat in bats)
        {
            var fileName = Path.GetFileName(bat);
            var id = PresetNaming.ToId(fileName);
            var batText = File.ReadAllText(bat);
            var arguments = BatPresetParser.ParseArguments(batText, id);
            var (name, description) = PresetNaming.Describe(id);
            var groupId = PresetNaming.TryParseGroupId(id, out var group)
                ? group
                : "general";
            presets.Add(new Preset(id, groupId, name, description, arguments));
        }

        var groups = PresetNaming.GroupsOrdered
            .Where(g => presets.Any(p => p.GroupId == g.Id))
            .Select(g => new PresetGroup(g.Id, g.Name))
            .ToList();

        return new PresetCatalog(1, "Flowseal/zapret-discord-youtube", engineVersion ?? "unknown", groups, presets);
    }
}
