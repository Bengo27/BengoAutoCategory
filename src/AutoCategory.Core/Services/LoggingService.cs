using AutoCategory.Core.Models;

namespace AutoCategory.Core.Services;

public interface ILoggingService
{
    event Action<ActivityLogEntry>? EntryAdded;
    IReadOnlyList<ActivityLogEntry> Snapshot();
    void Log(LogLevel level, string message, string? detail = null);
    void Clear();
    bool SaveToFile { get; set; }
    string LogDirectory { get; }
}

/// <summary>
/// יומן פעילות: שומר בזיכרון את 500 האירועים האחרונים, ואופציונלית כותב לקובץ יומי.
/// </summary>
public sealed class LoggingService : ILoggingService
{
    public const int MaxEntries = 500;

    private readonly object _lock = new();
    private readonly LinkedList<ActivityLogEntry> _entries = new();
    private readonly Func<DateTime> _now;

    public event Action<ActivityLogEntry>? EntryAdded;
    public bool SaveToFile { get; set; } = true;
    public string LogDirectory { get; }

    public LoggingService(string logDirectory, Func<DateTime>? now = null)
    {
        LogDirectory = logDirectory;
        _now = now ?? (() => DateTime.Now);
    }

    public IReadOnlyList<ActivityLogEntry> Snapshot()
    {
        lock (_lock) return _entries.ToList();
    }

    public void Log(LogLevel level, string message, string? detail = null)
    {
        var entry = new ActivityLogEntry(_now(), level, message, detail);
        lock (_lock)
        {
            _entries.AddLast(entry);
            while (_entries.Count > MaxEntries) _entries.RemoveFirst();
            if (SaveToFile) TryWriteToFile(entry);
        }
        EntryAdded?.Invoke(entry);
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    private void TryWriteToFile(ActivityLogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var file = Path.Combine(LogDirectory, $"activity-{entry.Timestamp:yyyy-MM-dd}.log");
            File.AppendAllText(file, entry + Environment.NewLine);
        }
        catch
        {
            // כתיבת לוג לעולם לא צריכה להפיל את התוכנה
        }
    }
}
