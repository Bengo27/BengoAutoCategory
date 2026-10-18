namespace AutoCategory.Core.Models;

public sealed class AppSettings
{
    /// <summary>כל כמה שניות בודקים את החלון הפעיל.</summary>
    public int DetectionIntervalSeconds { get; set; } = 2;

    /// <summary>כמה שניות משחק חדש צריך להיות בחזית לפני שינוי קטגוריה.</summary>
    public int VerificationDelaySeconds { get; set; } = 20;

    /// <summary>זמן מינימלי בין שני שינויי קטגוריה אוטומטיים (הגבלת קצב).</summary>
    public int MinSecondsBetweenChanges { get; set; } = 120;

    /// <summary>לחזור לקטגוריית ברירת המחדל כשאין משחק פעיל?</summary>
    public bool ReturnToDefaultEnabled { get; set; } = true;

    /// <summary>אחרי כמה דקות בלי משחק לחזור לקטגוריית ברירת המחדל.</summary>
    public int ReturnToDefaultMinutes { get; set; } = 5;

    /// <summary>קטגוריית ברירת המחדל (כשלא משחקים).</summary>
    public string DefaultCategory { get; set; } = "Just Chatting";

    /// <summary>הקטגוריה האחרונה הידועה של הסטרים (נשמרת בין הפעלות).</summary>
    public string CurrentCategory { get; set; } = "Just Chatting";

    public bool AutoCategoryEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool SaveLogsToFile { get; set; } = true;

    /// <summary>
    /// כשמחוברים לקיק: true = שינוי קטגוריה אמיתי בערוץ, false = סימולציה בלבד (לבדיקות).
    /// כשלא מחוברים – תמיד סימולציה.
    /// </summary>
    public bool KickLiveMode { get; set; } = true;

    /// <summary>אם לקטגוריה של משחק אין התאמה בקיק – לעבור לקטגוריית גיבוי במקום להיכשל.</summary>
    public bool FallbackCategoryEnabled { get; set; } = true;

    /// <summary>קטגוריית הגיבוי (בקיק: משחקים בלי קטגוריה משלהם, דמואים וכו').</summary>
    public string FallbackCategory { get; set; } = "Games + Demos";

    /// <summary>משחק לא מוכר שמותקן בתיקיית משחקים (Steam, Epic...) – להוסיף לו כלל אוטומטית.</summary>
    public bool AutoDetectGames { get; set; } = true;

    /// <summary>תיקיות משחקים נוספות של המשתמש (למשל D:\Games).</summary>
    public List<string> ExtraGameFolders { get; set; } = new();

    public AppSettings Clone()
    {
        var c = (AppSettings)MemberwiseClone();
        c.ExtraGameFolders = new List<string>(ExtraGameFolders ?? new());
        return c;
    }

    /// <summary>מתקן ערכים לא הגיוניים (למשל אחרי עריכה ידנית של הקובץ).</summary>
    public void Sanitize()
    {
        DetectionIntervalSeconds = Math.Clamp(DetectionIntervalSeconds, 1, 30);
        VerificationDelaySeconds = Math.Clamp(VerificationDelaySeconds, 3, 600);
        MinSecondsBetweenChanges = Math.Clamp(MinSecondsBetweenChanges, 0, 3600);
        ReturnToDefaultMinutes = Math.Clamp(ReturnToDefaultMinutes, 1, 240);
        if (string.IsNullOrWhiteSpace(DefaultCategory)) DefaultCategory = "Just Chatting";
        if (string.IsNullOrWhiteSpace(CurrentCategory)) CurrentCategory = DefaultCategory;
        if (string.IsNullOrWhiteSpace(FallbackCategory)) FallbackCategory = "Games + Demos";
        ExtraGameFolders = (ExtraGameFolders ?? new()).Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim().TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
