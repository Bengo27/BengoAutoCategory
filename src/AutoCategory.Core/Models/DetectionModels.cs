namespace AutoCategory.Core.Models;

/// <summary>תמונת מצב של החלון הפעיל (בחזית) ברגע מסוים.</summary>
public sealed record ForegroundInfo(
    string ProcessName,
    string ExecutableName,
    string WindowTitle,
    int ProcessId,
    DateTime Timestamp)
{
    /// <summary>הנתיב המלא של קובץ ההרצה (אם ידוע) – משמש לזיהוי משחקים לפי תיקיית ההתקנה.</summary>
    public string? ExecutablePath { get; init; }

    public static ForegroundInfo None(DateTime now) => new("", "", "", 0, now);
    public bool IsEmpty => string.IsNullOrEmpty(ExecutableName);
}

public enum DetectionKind
{
    /// <summary>אין חלון פעיל / לא ניתן לקרוא.</summary>
    None,
    /// <summary>תוכנה ברשימת ההתעלמות (OBS, Discord, דפדפן...).</summary>
    Ignored,
    /// <summary>תוכנה שאין לה כלל מיפוי.</summary>
    Unknown,
    /// <summary>משחק מוכר.</summary>
    Game,
}

public sealed class DetectionResult
{
    public required ForegroundInfo Foreground { get; init; }
    public required DetectionKind Kind { get; init; }

    /// <summary>הקטגוריה שזוהתה בחזית (רק כש-Kind == Game).</summary>
    public string? Category { get; init; }

    /// <summary>ודאות 0–100.</summary>
    public int Confidence { get; init; }

    public GameMapping? Mapping { get; init; }

    /// <summary>כלל האתר שהתאים (כשהזיהוי היה לפי לשונית בדפדפן).</summary>
    public SiteRule? Site { get; init; }

    /// <summary>
    /// משחק מוכר שרץ ברקע (גם אם הוא לא בחזית). משמש רק כדי "להחזיק" את הקטגוריה הנוכחית
    /// ולמנוע חזרה ל-Just Chatting – לעולם לא כדי להתחיל שינוי קטגוריה.
    /// </summary>
    public IReadOnlyList<string> BackgroundCategories { get; init; } = Array.Empty<string>();

    public bool AnyGameRunning => Kind == DetectionKind.Game || BackgroundCategories.Count > 0;
}
