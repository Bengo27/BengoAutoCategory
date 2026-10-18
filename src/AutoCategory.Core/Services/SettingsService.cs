using AutoCategory.Core.Models;
using AutoCategory.Core.Storage;

namespace AutoCategory.Core.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    event Action<AppSettings>? Changed;
    void Save(AppSettings settings);
    /// <summary>עדכון קטן ושמירה (למשל שינוי הקטגוריה הנוכחית).</summary>
    void Update(Action<AppSettings> change);
    string FilePath { get; }
}

public sealed class SettingsService : ISettingsService
{
    private readonly object _lock = new();
    private AppSettings _current;

    public string FilePath { get; }
    public event Action<AppSettings>? Changed;

    public SettingsService(string dataDirectory, ILoggingService? log = null)
    {
        FilePath = Path.Combine(dataDirectory, "settings.json");
        try
        {
            _current = JsonStore.Load<AppSettings>(FilePath) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            JsonStore.BackupCorrupt(FilePath);
            log?.Log(LogLevel.Warning, "קובץ ההגדרות פגום – נטענו הגדרות ברירת מחדל", ex.Message);
            _current = new AppSettings();
        }
        _current.Sanitize();
        if (!File.Exists(FilePath)) TrySave(_current);
    }

    public AppSettings Current
    {
        get { lock (_lock) return _current.Clone(); }
    }

    public void Save(AppSettings settings)
    {
        var copy = settings.Clone();
        copy.Sanitize();
        lock (_lock)
        {
            _current = copy;
            TrySave(copy);
        }
        Changed?.Invoke(copy.Clone());
    }

    public void Update(Action<AppSettings> change)
    {
        var s = Current;
        change(s);
        Save(s);
    }

    private void TrySave(AppSettings s)
    {
        try { JsonStore.Save(FilePath, s); } catch { /* נשמר בפעם הבאה */ }
    }
}
