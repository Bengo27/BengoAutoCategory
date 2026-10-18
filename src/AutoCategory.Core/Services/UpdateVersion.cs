namespace AutoCategory.Core.Services;

/// <summary>השוואת גרסאות לעדכון אוטומטי ("v0.3.1" מול 0.2.0).</summary>
public static class UpdateVersion
{
    public static Version? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim().TrimStart('v', 'V');
        var dash = t.IndexOfAny(new[] { '-', '+', ' ' });
        if (dash >= 0) t = t[..dash];
        if (!Version.TryParse(t.Contains('.') ? t : t + ".0", out var v)) return null;
        // 0.3 → 0.3.0.0 כדי להשוות נכון
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
    }

    public static bool IsNewer(string? latestTag, Version current)
    {
        var latest = Parse(latestTag);
        if (latest is null) return false;
        var cur = new Version(current.Major, current.Minor, Math.Max(0, current.Build), Math.Max(0, current.Revision));
        return latest > cur;
    }
}
