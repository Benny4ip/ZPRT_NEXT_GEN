namespace ZprtNextGen.Core.Updates;

/// <summary>Сравнение семантических версий (с опциональным префиксом <c>v</c>).</summary>
public static class VersionComparer
{
    /// <summary>Сравнивает две версии. Возвращает -1, 0 или 1.</summary>
    public static int Compare(string? a, string? b)
    {
        var pa = Parse(a);
        var pb = Parse(b);

        if (pa is null && pb is null) return 0;
        if (pa is null) return -1;
        if (pb is null) return 1;

        var (va, preA) = pa.Value;
        var (vb, preB) = pb.Value;

        var length = Math.Max(va.Length, vb.Length);
        for (var i = 0; i < length; i++)
        {
            var x = i < va.Length ? va[i] : 0;
            var y = i < vb.Length ? vb[i] : 0;
            if (x != y)
                return x < y ? -1 : 1;
        }

        // Числовые части равны: релиз без суффикса новее, чем предрелизная версия (SemVer)
        if (preA is null && preB is not null) return 1;
        if (preA is not null && preB is null) return -1;
        if (preA is not null && preB is not null)
            return StringComparer.OrdinalIgnoreCase.Compare(preA, preB);

        return 0;
    }

    /// <summary><c>true</c>, если <c>candidate</c> строго новее <c>current</c>.</summary>
    public static bool IsNewer(string? candidate, string? current) => Compare(candidate, current) > 0;

    private static (int[] numbers, string? prerelease)? Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var cleaned = version.Trim().TrimStart('v', 'V');
        string? prerelease = null;
        var dashIndex = cleaned.IndexOf('-');
        if (dashIndex >= 0)
        {
            prerelease = cleaned[(dashIndex + 1)..];
            cleaned = cleaned[..dashIndex];
        }

        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var result = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out result[i]))
                return null;
        }

        return (result, prerelease);
    }
}
