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

    /// <summary>
    /// בוחר מתוך רשימת ה-Releases של GitHub את הגרסה האחרונה של התוכנה הזו בלבד
    /// (Catego: תגיות v0.4.1 עם Catego.exe; Studio: תגיות studio-v0.1.0 עם CategoStudio.exe).
    /// </summary>
    public static ReleaseCandidate? PickRelease(string releasesJson, string tagPrefix, string assetName)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(releasesJson);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
        ReleaseCandidate? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (Bool(r, "draft") || Bool(r, "prerelease")) continue;
            var tag = Str(r, "tag_name") ?? "";
            if (!tag.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var version = Parse(tag[tagPrefix.Length..]);
            if (version is null) continue;
            if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
            foreach (var a in assets.EnumerateArray())
            {
                if (!string.Equals(Str(a, "name"), assetName, StringComparison.OrdinalIgnoreCase)) continue;
                var url = Str(a, "browser_download_url");
                if (string.IsNullOrEmpty(url)) continue;
                var size = a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var n) ? n : 0;
                if (best is null || version > best.Version)
                    best = new ReleaseCandidate(tag, version, Str(r, "name") ?? tag, Str(r, "body") ?? "", url, size, Str(r, "html_url") ?? "");
                break;
            }
        }
        return best;
    }

    private static string? Str(System.Text.Json.JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(System.Text.Json.JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
}

public sealed record ReleaseCandidate(string Tag, Version Version, string Name, string Notes, string DownloadUrl, long Size, string PageUrl);
