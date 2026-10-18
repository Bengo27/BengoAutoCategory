namespace AutoCategory.Core.Models;

public enum LogLevel
{
    Info,
    Detected,
    Ignored,
    Pending,
    Confirmed,
    Change,
    Cancelled,
    Warning,
}

public sealed record ActivityLogEntry(DateTime Timestamp, LogLevel Level, string Message, string? Detail = null)
{
    public string TimeText => Timestamp.ToString("HH:mm:ss");

    public string LevelText => Level switch
    {
        LogLevel.Detected => "זיהוי",
        LogLevel.Ignored => "התעלמות",
        LogLevel.Pending => "ממתין",
        LogLevel.Confirmed => "אומת",
        LogLevel.Change => "שינוי",
        LogLevel.Cancelled => "בוטל",
        LogLevel.Warning => "אזהרה",
        _ => "מידע",
    };

    public override string ToString() =>
        Detail is null
            ? $"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message}"
            : $"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message} | {Detail}";
}
