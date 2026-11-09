namespace ZprtNextGen.Core.Updates;

/// <summary>Сравнение семантических версий (с опциональным префиксом <c>v</c>).</summary>
public static class VersionComparer
{
    /// <summary>Сравнивает две версии. Возвращает -1, 0 или 1.</summary>
    public static int Compare(string? a, string? b)
    {
        var va = Parse(a);
        var vb = Parse(b);

        if (va is null && vb is null) return 0;
        if (va is null) return -1;
        if (vb is null) return 1;

        var length = Math.Max(va.Length, vb.Length);
        for (var i = 0; i < length; i++)
        {
            var x = i < va.Length ? va[i] : 0;
            var y = i < vb.Length ? vb[i] : 0;
            if (x != y)
                return x < y ? -1 : 1;
        }
        return 0;
    }

    /// <summary><c>true</c>, если <c>candidate</c> строго новее <c>current</c>.</summary>
    public static bool IsNewer(string? candidate, string? current) => Compare(candidate, current) > 0;

    private static int[]? Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var cleaned = version.Trim().TrimStart('v', 'V');
        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var result = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out result[i]))
                return null;
        }

        return result;
    }
}
