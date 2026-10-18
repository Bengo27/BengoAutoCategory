using System.Text.Json.Serialization;

namespace AutoCategory.Core.Models;

/// <summary>
/// כלל מיפוי: קובץ הרצה (exe) → קטגוריה בסטרים.
/// </summary>
public sealed class GameMapping
{
    /// <summary>שם קובץ ההרצה, למשל GTA5.exe (לא תלוי באותיות גדולות/קטנות).</summary>
    public string Executable { get; set; } = "";

    /// <summary>שם הקטגוריה כפי שתוצג בסטרים, למשל Grand Theft Auto V.</summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// אופציונלי: טקסט שחייב להופיע בכותרת החלון כדי שהכלל יתאים.
    /// משמש לקבצים "דו-משמעיים" כמו javaw.exe (שמריץ כל תוכנת Java, לא רק Minecraft).
    /// </summary>
    public string? WindowTitleContains { get; set; }

    /// <summary>
    /// קובץ הרצה דו-משמעי (למשל javaw.exe). התאמה כזו מקבלת ודאות של 80% במקום 100%,
    /// ולא משמשת לזיהוי משחק שרץ ברקע.
    /// </summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>
    /// שמור לשלב 2: מזהה הקטגוריה בקיק (category_id). בשלב 1 לא בשימוש.
    /// </summary>
    public string? KickCategoryId { get; set; }

    /// <summary>הערה חופשית (למשל "שם הקובץ טרם אומת").</summary>
    public string? Notes { get; set; }

    [JsonIgnore]
    public string NormalizedExecutable => Normalize(Executable);

    public static string Normalize(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return "";
        var s = exe.Trim();
        // אם הוזן נתיב מלא – לוקחים רק את שם הקובץ
        var slash = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
        if (slash >= 0) s = s[(slash + 1)..];
        if (!s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s += ".exe";
        return s.ToLowerInvariant();
    }

    public GameMapping Clone() => new()
    {
        Executable = Executable,
        Category = Category,
        WindowTitleContains = WindowTitleContains,
        IsAmbiguous = IsAmbiguous,
        KickCategoryId = KickCategoryId,
        Notes = Notes,
    };
}
