using AutoCategory.Core.Models;

namespace AutoCategory.Core.Services;

/// <summary>
/// מקור המידע על החלון הפעיל. המימוש האמיתי (Win32) נמצא בפרויקט האפליקציה,
/// כך שמנוע הזיהוי לא תלוי ב-Windows וניתן לבדיקה אוטומטית.
/// </summary>
public interface IForegroundWindowService
{
    ForegroundInfo GetForeground();

    /// <summary>שמות קבצי ההרצה של כל התהליכים שרצים כרגע.</summary>
    IReadOnlyCollection<string> GetRunningExecutables();
}

/// <summary>
/// מצב בדיקה: מחזיר "תוכנה מזויפת" שנבחרה ידנית, בלי צורך להתקין משחקים.
/// </summary>
public sealed class FakeForegroundWindowService : IForegroundWindowService
{
    private readonly object _lock = new();
    private string _exe = "explorer.exe";
    private string _title = "";
    private string? _path;
    private readonly Func<DateTime> _now;

    public FakeForegroundWindowService(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.Now);

    public string CurrentExecutable { get { lock (_lock) return _exe; } }

    public void SetForeground(string executable, string? windowTitle = null, string? path = null)
    {
        lock (_lock)
        {
            _exe = executable;
            _title = windowTitle ?? DefaultTitle(executable);
            _path = path ?? DefaultPath(executable);
        }
    }

    public ForegroundInfo GetForeground()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_exe)) return ForegroundInfo.None(_now());
            var name = _exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? _exe[..^4] : _exe;
            return new ForegroundInfo(name, _exe, _title, 0, _now()) { ExecutablePath = _path };
        }
    }

    public IReadOnlyCollection<string> GetRunningExecutables()
    {
        lock (_lock) return string.IsNullOrEmpty(_exe) ? Array.Empty<string>() : new[] { _exe };
    }

    private static string DefaultTitle(string exe) => exe.ToLowerInvariant() switch
    {
        "gta5.exe" => "Grand Theft Auto V",
        "valorant-win64-shipping.exe" => "VALORANT",
        "cs2.exe" => "Counter-Strike 2",
        "javaw.exe" => "Minecraft 1.21.4",
        "chrome.exe" => "YouTube - Google Chrome",
        "discord.exe" => "Discord",
        "obs64.exe" => "OBS 31.0.0 - Profile: Untitled",
        "unknown-game.exe" => "Unknown Game",
        "myindiegame.exe" => "My Indie Game",
        _ => "",
    };

    /// <summary>נתיב מדומה – כדי לבדוק זיהוי משחק לפי תיקיית Steam.</summary>
    private static string? DefaultPath(string exe) => exe.ToLowerInvariant() switch
    {
        "myindiegame.exe" => @"D:\SteamLibrary\steamapps\common\My Indie Game\MyIndieGame.exe",
        _ => null,
    };
}
