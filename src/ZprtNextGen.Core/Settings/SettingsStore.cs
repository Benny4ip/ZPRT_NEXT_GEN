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
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            MoveToBadIgnoreFailure();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempFile = Path + ".tmp";
        // File.WriteAllText пишет UTF-8 без BOM.
        File.WriteAllText(tempFile, json);
        File.Move(tempFile, Path, overwrite: true);
    }

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
