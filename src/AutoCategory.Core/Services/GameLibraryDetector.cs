namespace AutoCategory.Core.Services;

/// <summary>תוצאה: זה משחק, מאיזו ספרייה, ומה השם שלו.</summary>
public sealed record GameLibraryMatch(string Library, string GameName, string FolderName);

/// <summary>
/// מזהה משחקים לא מוכרים לפי תיקיית ההתקנה שלהם (Steam, Epic, Riot, EA, Ubisoft, GOG, Xbox,
/// ותיקיות שהמשתמש הוסיף). תוכנה רגילה שלא נמצאת בתיקיות האלה – לא נחשבת משחק.
/// </summary>
public static class GameLibraryDetector
{
    /// <summary>תבנית בנתיב → שם הספרייה. התיקייה שמיד אחרי התבנית היא תיקיית המשחק.</summary>
    private static readonly (string Marker, string Library)[] Libraries =
    {
        (@"\steamapps\common\", "Steam"),
        (@"\epic games\", "Epic Games"),
        (@"\riot games\", "Riot"),
        (@"\ea games\", "EA"),
        (@"\origin games\", "EA"),
        (@"\ubisoft game launcher\games\", "Ubisoft"),
        (@"\gog galaxy\games\", "GOG"),
        (@"\gog games\", "GOG"),
        (@"\xboxgames\", "Xbox"),
        (@"\battle.net games\", "Battle.net"),
        (@"\rockstar games\", "Rockstar"),
    };

    /// <summary>קבצים בתוך תיקיות משחקים שהם לא המשחק עצמו.</summary>
    private static readonly string[] HelperFragments =
    {
        "crash", "report", "launcher", "setup", "install", "uninst", "redist", "vcredist", "dxsetup",
        "prereq", "easyanticheat", "battleye", "beservice", "updater", "patcher", "helper", "unitycrashhandler",
        "cefprocess", "webhelper", "overlay", "config",
    };

    /// <summary>שמות תיקייה שהם לא שם של משחק (למשל XboxGames\Halo\Content\...).</summary>
    private static readonly HashSet<string> NonGameFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "launcher", "redist", "_commonredist", "directx", "steamworks shared", "common", "content", "binaries",
    };

    public static GameLibraryMatch? Match(string? executablePath, string? windowTitle, IEnumerable<string>? extraFolders = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        var path = executablePath.Replace('/', '\\');
        var lower = path.ToLowerInvariant();

        var exeName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (HelperFragments.Any(h => exeName.Contains(h, StringComparison.Ordinal))) return null;

        foreach (var (marker, library) in Libraries)
        {
            var i = lower.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) continue;
            var folder = FirstFolderAfter(path, i + marker.Length);
            if (folder is null || NonGameFolders.Contains(folder)) continue;
            return new GameLibraryMatch(library, PickName(windowTitle, folder), folder);
        }

        foreach (var extra in extraFolders ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(extra)) continue;
            var root = extra.Replace('/', '\\').TrimEnd('\\') + "\\";
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            var folder = FirstFolderAfter(path, root.Length);
            // exe ישירות בתיקייה הראשית – משתמשים בשם הקובץ
            folder ??= Path.GetFileNameWithoutExtension(path);
            if (NonGameFolders.Contains(folder)) continue;
            return new GameLibraryMatch("תיקיית משחקים", PickName(windowTitle, folder), folder);
        }

        return null;
    }

    private static string? FirstFolderAfter(string path, int start)
    {
        if (start >= path.Length) return null;
        var end = path.IndexOf('\\', start);
        if (end < 0) return null; // הקובץ עצמו, לא תיקייה
        var folder = path[start..end].Trim();
        return folder.Length == 0 ? null : folder;
    }

    /// <summary>שם המשחק: כותרת החלון אם היא נראית כמו שם, אחרת שם התיקייה.</summary>
    public static string PickName(string? windowTitle, string folder)
    {
        var title = (windowTitle ?? "").Trim();
        if (title.Length is >= 2 and <= 60 && title.Any(char.IsLetter) && !title.Contains('\\') &&
            !title.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return title;
        return folder.Replace('_', ' ').Trim();
    }
}
