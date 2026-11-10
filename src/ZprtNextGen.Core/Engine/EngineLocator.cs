namespace ZprtNextGen.Core.Engine;

using System.IO;

/// <summary>
/// Поиск и валидация директории с движком zapret (winws.exe и сопровождающие файлы).
/// </summary>
public static class EngineLocator
{
    /// <summary>Обязательные файлы движка, относительно корня директории движка.</summary>
    public static IReadOnlyList<string> RequiredFiles { get; } = new[]
    {
        @"bin\winws.exe",
        @"bin\WinDivert.dll",
        @"bin\WinDivert64.sys",
        @"lists\list-general.txt"
    };

    /// <summary>
    /// Ищет валидную директорию движка среди кандидатов в порядке приоритета:
    /// заданный пользователем путь (configuredPath), затем <c>baseDirectory\engine</c>,
    /// затем сам <c>baseDirectory</c>. Кандидаты дедуплицируются по полному пути;
    /// побеждает первый кандидат, содержащий все обязательные файлы.
    /// </summary>
    /// <param name="configuredPath">Путь к движку из настроек (может быть <c>null</c> или пустым).</param>
    /// <param name="baseDirectory">Базовая директория приложения.</param>
    /// <returns>Результат валидации. При неудаче <see cref="EngineValidation.IsValid"/> = <c>false</c>,
    /// <see cref="EngineValidation.EngineRoot"/> = <c>null</c>, а <see cref="EngineValidation.MissingFiles"/>
    /// содержит отсутствующие файлы первого кандидата (имена относительно его корня).
    /// </returns>
    public static EngineValidation Locate(string? configuredPath, string baseDirectory)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrEmpty(configuredPath))
            candidates.Add(configuredPath);

        candidates.Add(Path.Combine(baseDirectory, "engine"));
        candidates.Add(baseDirectory);

        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Невалидный путь в настройках — пропускаем, не роняя поиск движка.
                continue;
            }

            if (seen.Add(fullPath))
                unique.Add(fullPath);
        }

        var firstCandidate = unique.Count > 0
            ? unique[0]
            : Path.GetFullPath(baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        foreach (var candidate in unique)
        {
            var missing = RequiredFiles
                .Where(file => !File.Exists(Path.Combine(candidate, file)))
                .ToList();

            if (missing.Count == 0)
                return new EngineValidation(true, candidate, []);
        }

        var missingAgainstFirst = RequiredFiles
            .Where(file => !File.Exists(Path.Combine(firstCandidate, file)))
            .ToList();

        return new EngineValidation(false, null, missingAgainstFirst);
    }
}
