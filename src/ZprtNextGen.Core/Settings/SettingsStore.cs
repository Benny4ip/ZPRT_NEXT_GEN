namespace ZprtNextGen.Core.Settings;

using System.Text.Json;
using System.Text.Json.Serialization;
using ZprtNextGen.Core.Presets;

/// <summary>
/// Хранилище пользовательских настроек в JSON-файле.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Путь к файлу настроек.</summary>
    string Path { get; }

    /// <summary>Загрузить настройки из файла.</summary>
    AppSettings Load();

    /// <summary>Сохранить настройки в файл.</summary>
    void Save(AppSettings settings);
}

/// <summary>
/// Реализация <see cref="ISettingsStore"/> на основе System.Text.Json.
/// Файл настроек восстанавливается из значений по умолчанию при любом
/// повреждении, чтобы приложение всегда могло запуститься.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _saveLock = new();

    public string Path { get; }

    public SettingsStore(string path)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>
    /// Хранилище по умолчанию: %APPDATA%\ZPRT_NEXT_GEN\settings.json.
    /// </summary>
    public static SettingsStore CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = System.IO.Path.Combine(appData, "ZPRT_NEXT_GEN");
        return new SettingsStore(System.IO.Path.Combine(directory, "settings.json"));
    }

    public AppSettings Load()
    {
        try
        {
            var json = File.ReadAllText(Path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return Normalize(loaded);
        }
        catch (JsonException)
        {
            // Повреждённый JSON: сохраняем копию и продолжаем с настройками по умолчанию.
            MoveToBadIgnoreFailure();
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Файл временно недоступен (блокировка/права) — не удаляем его, просто используем значения по умолчанию.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Сериализация/запись могут вызываться из разных потоков (UI, автообновление, трей).
        lock (_saveLock)
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempFile = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                // File.WriteAllText пишет UTF-8 без BOM.
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, Path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* лучшая попытка */ }
                throw;
            }
        }
    }

    /// <summary>Привести настройки, прочитанные из JSON, к допустимым значениям.</summary>
    private static AppSettings Normalize(AppSettings settings) => settings with
    {
        EnginePath = string.IsNullOrWhiteSpace(settings.EnginePath) ? null : settings.EnginePath,
        PresetId = string.IsNullOrWhiteSpace(settings.PresetId) ? "general" : settings.PresetId,
        Theme = string.IsNullOrWhiteSpace(settings.Theme) ? "dark" : settings.Theme,
        CollapsedGroups = settings.CollapsedGroups ?? [],
    };

    private void MoveToBadIgnoreFailure()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Move(Path, Path + ".bad", overwrite: true);
            }
        }
        catch
        {
            // Неудача переименования битого файла не должна ломать запуск.
        }
    }
}
