using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.PresetTool;

/// <summary>CLI-инструмент импорта каталога пресетов из папки движка zapret.</summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    public static int Main(string[] args)
    {
        string? engineRoot = null;
        string? outPath = null;
        string? engineVersion = null;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
            switch (arg)
            {
                case "--engine":
                    engineRoot = RequireValue(args, ref i, arg);
                    break;
                case "--out":
                    outPath = RequireValue(args, ref i, arg);
                    break;
                case "--engine-version":
                    engineVersion = RequireValue(args, ref i, arg);
                    break;
                default:
                    throw new ArgumentException($"Неизвестный аргумент: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(engineRoot))
        {
            Console.Error.WriteLine("Ошибка импорта: не задан обязательный аргумент --engine.");
            return ExitFailure;
        }

        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.Error.WriteLine("Ошибка импорта: не задан обязательный аргумент --out.");
            return ExitFailure;
        }

            var catalog = PresetImporter.Import(engineRoot, engineVersion ?? "unknown");

            var dto = new ImportDto
            {
                Version = 1,
                Engine = catalog.Engine,
                EngineVersion = catalog.EngineVersion,
                Groups = catalog.Groups
                    .Select(g => new GroupDto { Id = g.Id, Name = g.Name })
                    .ToList(),
                Presets = catalog.Presets
                    .Select(p => new PresetDto
                    {
                        Id = p.Id,
                        Group = p.GroupId,
                        Name = p.Name,
                        Description = p.Description,
                        Args = p.Arguments
                    })
                    .ToList(),
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(dto, options);

            var directory = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.AppendAllText(outPath, Environment.NewLine);
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка импорта: {ex.Message}");
            return ExitFailure;
        }
    }

    private static string RequireValue(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Для аргумента {option} не указано значение.");
        }

        return args[++i];
    }

    private sealed class ImportDto
    {
        [JsonPropertyName("version")]
        public int Version { get; init; }

        [JsonPropertyName("engine")]
        public string Engine { get; init; } = string.Empty;

        [JsonPropertyName("engineVersion")]
        public string EngineVersion { get; init; } = string.Empty;

        [JsonPropertyName("groups")]
        public List<GroupDto> Groups { get; init; } = new();

        [JsonPropertyName("presets")]
        public List<PresetDto> Presets { get; init; } = new();
    }

    private sealed class GroupDto
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class PresetDto
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("group")]
        public string Group { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("args")]
        public string Args { get; init; } = string.Empty;
    }

}

