namespace ZprtNextGen.Core.Engine;

using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using ZprtNextGen.Core.Logging;

/// <summary>Результат подготовки portable-движка.</summary>
public sealed record PortableEngineResult(string EngineRoot, string PresetsPath, bool Installed);

/// <summary>
/// Распаковывает встроенный в portable-сборку движок в кэш
/// <c>%LOCALAPPDATA%\ZPRT_NEXT_GEN\engine\&lt;версия&gt;-&lt;hash8&gt;</c>.
/// В Standard-сборке ресурса нет, и установщик ничего не делает.
/// </summary>
public sealed class PortableEngineInstaller
{
    /// <summary>Имя embedded resource с zip-архивом движка.</summary>
    public const string EmbeddedResourceName = "ZprtNextGen.Engine.zip";

    /// <summary>Переменная окружения для подмены корня кэша (тесты и build-скрипт).</summary>
    public const string CacheRootEnvironmentVariable = "ZPRT_ENGINE_CACHE_ROOT";

    private const string ReadyMarker = ".ready";
    private const string PresetsRelative = @"presets\presets.json";

    private readonly byte[]? _zip;
    private readonly string _version;
    private readonly string _cacheRoot;
    private readonly IAppLogger? _logger;

    public PortableEngineInstaller(
        byte[]? embeddedZip, string version, string? cacheRoot = null, IAppLogger? logger = null)
    {
        _zip = embeddedZip;
        _version = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim();
        _cacheRoot = string.IsNullOrWhiteSpace(cacheRoot) ? DefaultCacheRoot() : cacheRoot!;
        _logger = logger;
    }

    /// <summary><c>true</c>, если в сборку встроен движок (portable-версия).</summary>
    public bool IsAvailable => _zip is { Length: > 0 };

    /// <summary>Прочитать архив движка из ресурсов сборки.</summary>
    public static byte[]? ReadEmbeddedEngine(Assembly assembly, string resourceName = EmbeddedResourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Корень кэша portable-движка.</summary>
    public static string DefaultCacheRoot()
    {
        var overridden = Environment.GetEnvironmentVariable(CacheRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
            return overridden!;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZPRT_NEXT_GEN",
            "engine");
    }

    /// <summary>
    /// Подготовить движок. <c>null</c> — это Standard-сборка без встроенного движка.
    /// </summary>
    public async Task<PortableEngineResult?> InstallAsync(CancellationToken ct)
    {
        if (!IsAvailable)
            return null;

        ct.ThrowIfCancellationRequested();

        var hash8 = ComputeHash8(_zip!);
        var target = Path.Combine(_cacheRoot, _version + "-" + hash8);
        var presetsPath = Path.Combine(target, PresetsRelative);

        if (IsReady(target))
        {
            _logger?.Info("Portable-движок уже распакован: " + target);
            RemoveOldVersions(target);
            return new PortableEngineResult(target, presetsPath, Installed: false);
        }

        Directory.CreateDirectory(_cacheRoot);

        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            Directory.CreateDirectory(temporary);
            Extract(_zip!, temporary);

            ct.ThrowIfCancellationRequested();

            var missing = MissingFiles(temporary);
            if (missing.Count > 0)
            {
                throw new InvalidDataException(
                    "Во встроенном движке не хватает файлов: " + string.Join(", ", missing));
            }

            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);

            Directory.Move(temporary, target);
            File.WriteAllText(Path.Combine(target, ReadyMarker), _version + " " + hash8);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        RemoveOldVersions(target);
        _logger?.Info("Portable-движок распакован: " + target);

        return new PortableEngineResult(target, presetsPath, Installed: true);
    }

    private static void Extract(byte[] zip, string destination)
    {
        using var memory = new MemoryStream(zip, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        archive.ExtractToDirectory(destination, overwriteFiles: true);
    }

    private static string ComputeHash8(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data))[..8].ToLowerInvariant();

    private static IReadOnlyList<string> RequiredFiles { get; } =
    [
        .. EngineLocator.RequiredFiles,
        PresetsRelative,
    ];

    private static List<string> MissingFiles(string root) =>
        RequiredFiles
            .Where(relative => !File.Exists(Path.Combine(root, relative)))
            .ToList();

    private static bool IsReady(string root) =>
        File.Exists(Path.Combine(root, ReadyMarker)) && MissingFiles(root).Count == 0;

    private void RemoveOldVersions(string current)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(_cacheRoot))
            {
                if (string.Equals(directory, current, StringComparison.OrdinalIgnoreCase))
                    continue;

                TryDelete(directory);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("Очистка старых portable-движков: " + ex.Message, ex);
        }
    }

    private void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            _logger?.Error($"Не удалось удалить «{directory}»: {ex.Message}", ex);
        }
    }
}
