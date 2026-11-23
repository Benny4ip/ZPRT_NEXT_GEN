using System.Security.Cryptography;
using System.Text;

namespace ZprtNextGen.Core.Updates;

/// <summary>Проверка SHA256 скачанных файлов и разбор <c>SHA256SUMS.txt</c>.</summary>
public static class Sha256Verifier
{
    /// <summary>Вычислить lowercase hex SHA256 файла.</summary>
    public static string HashFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Вычислить lowercase hex SHA256 буфера.</summary>
    public static string HashBytes(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>
    /// Нормализовать digest вида <c>sha256:ABC</c> или сырой hex к lowercase hex без префикса.
    /// </summary>
    public static string NormalizeDigest(string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);
        var value = digest.Trim();
        const string prefix = "sha256:";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            value = value[prefix.Length..];
        return value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Разобрать файл сумм (<c>hash  filename</c> или <c>hash *filename</c>).
    /// Ключ словаря — имя файла (без пути), значение — hex hash.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseSums(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var hash = NormalizeDigest(parts[0]);
            var name = parts[1].Trim();
            if (name.StartsWith('*'))
                name = name[1..].Trim();
            name = Path.GetFileName(name.Replace('\\', '/'));
            if (hash.Length == 0 || name.Length == 0)
                continue;
            map[name] = hash;
        }

        return map;
    }

    /// <summary>Найти ожидаемый хеш для <paramref name="fileName"/> в словаре сумм.</summary>
    public static string? FindExpected(IReadOnlyDictionary<string, string> sums, string fileName)
    {
        ArgumentNullException.ThrowIfNull(sums);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var key = Path.GetFileName(fileName);
        return sums.TryGetValue(key, out var hash) ? hash : null;
    }

    /// <summary>
    /// Проверить файл: при несовпадении бросает <see cref="InvalidDataException"/>.
    /// </summary>
    public static void VerifyFile(string path, string expectedDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var expected = NormalizeDigest(expectedDigest);
        var actual = HashFile(path);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"SHA256 не совпал для {Path.GetFileName(path)}: ожидалось {expected}, получено {actual}.");
        }
    }

    /// <summary>Собрать текст <c>SHA256SUMS.txt</c> (две пробела между hash и именем).</summary>
    public static string FormatSums(IEnumerable<(string FileName, string Hash)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sb = new StringBuilder();
        foreach (var (fileName, hash) in entries)
        {
            sb.Append(NormalizeDigest(hash));
            sb.Append("  ");
            sb.Append(Path.GetFileName(fileName));
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
