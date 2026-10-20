using System.Text.Json;
using AutoCategory.Core.Storage;

namespace AutoCategory.Core.NowPlaying;

/// <summary>מאיפה יודעים מה מתנגן.</summary>
public enum NowPlayingSource
{
    /// <summary>מ-Windows (מה שמוצג בפקדי המדיה של Windows) – בלי התחברות, בלי Premium.</summary>
    Windows,
    /// <summary>מה-API של ספוטיפיי – דורש Client ID משלך.</summary>
    Spotify,
}

/// <summary>השיר שמתנגן עכשיו.</summary>
public sealed record NowPlayingTrack(
    string Id,
    string Title,
    string Artist,
    string? Image,
    string? Url,
    long DurationMs,
    long ProgressMs,
    bool Playing,
    DateTime AtUtc);

/// <summary>תוצאה של קריאה אחת מהמקור.</summary>
public sealed record NowPlayingRead(NowPlayingTrack? Track, string? Error, TimeSpan NextPoll, byte[]? Art = null, string? ArtType = null);

public interface INowPlayingSource
{
    Task<NowPlayingRead> ReadAsync(CancellationToken ct);
}

/// <summary>הגדרות הווידג'ט (now_playing.json). העיצובים הם של הפרויקט המקורי של Bengo.</summary>
public sealed class NowPlayingSettings
{
    public static readonly IReadOnlyList<string> Themes = new[] { "classic", "minimal", "glass", "neon", "album", "slim", "vinyl" };

    public NowPlayingSource Source { get; set; } = NowPlayingSource.Windows;
    /// <summary>במקור Windows: להציג רק את ספוטיפיי (ולא YouTube בדפדפן וכו').</summary>
    public bool OnlySpotify { get; set; } = true;
    public string Theme { get; set; } = "classic";
    public string Accent { get; set; } = "#a6ef67";
    public double Opacity { get; set; } = 0.94;
    public double Scale { get; set; } = 1;
    public bool HidePaused { get; set; } = true;
    public bool Compact { get; set; }

    public void Sanitize()
    {
        if (!Themes.Contains(Theme)) Theme = "classic";
        if (Accent is null || !System.Text.RegularExpressions.Regex.IsMatch(Accent, "^#[0-9a-fA-F]{6}$")) Accent = "#a6ef67";
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.15, 1) : 0.94;
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.6, 1.3) : 1;
    }
}

/// <summary>קובץ הגדרות JSON פשוט עם אירוע שינוי.</summary>
public sealed class JsonFileStore<T> where T : class, new()
{
    private readonly object _lock = new();
    private readonly Action<T>? _sanitize;
    private T _current;
    public string FilePath { get; }
    public event Action<T>? Changed;

    public JsonFileStore(string filePath, Action<T>? sanitize = null)
    {
        FilePath = filePath;
        _sanitize = sanitize;
        try { _current = JsonStore.Load<T>(filePath) ?? new T(); }
        catch { JsonStore.BackupCorrupt(filePath); _current = new T(); }
        _sanitize?.Invoke(_current);
    }

    public T Current { get { lock (_lock) return Copy(_current); } }

    public void Update(Action<T> change)
    {
        T copy;
        lock (_lock)
        {
            copy = Copy(_current);
            change(copy);
            _sanitize?.Invoke(copy);
            _current = copy;
            try { JsonStore.Save(FilePath, copy); } catch { }
        }
        Changed?.Invoke(Copy(copy));
    }

    private static T Copy(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonStore.Options), JsonStore.Options)!;
}
