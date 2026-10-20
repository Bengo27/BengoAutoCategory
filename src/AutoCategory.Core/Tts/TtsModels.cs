using AutoCategory.Core.Services;
using AutoCategory.Core.Storage;

namespace AutoCategory.Core.Tts;

/// <summary>איפה מושמעת ההקראה.</summary>
public enum TtsPlayback
{
    /// <summary>במחשב (ברמקולים/אוזניות). OBS קולט את זה כ-Desktop Audio.</summary>
    App,
    /// <summary>בתוך הווידג'ט ב-OBS (Browser Source) – ערוץ שמע נפרד עם עוצמה משלו.</summary>
    Widget,
}

/// <summary>הגדרות ההקראה (נשמרות ב-tts_settings.json).</summary>
public sealed class TtsSettings
{
    public bool Enabled { get; set; }

    // ── הפרס בקיק ──
    public string? RewardId { get; set; }
    public string RewardTitle { get; set; } = "🔊 הקראה";
    public int RewardCost { get; set; } = 500;
    public string RewardDescription { get; set; } = "כתבו הודעה והיא תוקרא בקול בשידור";

    // ── קול ──
    public string Voice { get; set; } = TtsVoices.Hila;
    /// <summary>מהירות: ‎-50 עד 50 (אחוזים).</summary>
    public int RatePercent { get; set; }
    /// <summary>עוצמה: 0–100.</summary>
    public int VolumePercent { get; set; } = 80;
    public TtsPlayback Playback { get; set; } = TtsPlayback.App;

    // ── מה מוקרא ──
    /// <summary>{user} = שם הצופה, {text} = ההודעה.</summary>
    public string Template { get; set; } = "{user} אומר: {text}";
    public int MaxChars { get; set; } = 200;
    public bool BlockLinks { get; set; } = true;
    public bool ManualApproval { get; set; }
    /// <summary>בקשה ישנה יותר (למשל כשהתוכנה הייתה סגורה) – נדחית והנקודות חוזרות.</summary>
    public int MaxAgeMinutes { get; set; } = 10;
    /// <summary>הפסקה בין הקראות (שניות).</summary>
    public int GapSeconds { get; set; } = 2;
    public List<string> BannedWords { get; set; } = TtsFilter.DefaultBannedWords.ToList();

    public TtsSettings Clone()
    {
        var c = (TtsSettings)MemberwiseClone();
        c.BannedWords = new List<string>(BannedWords ?? new());
        return c;
    }

    public void Sanitize()
    {
        RewardCost = Math.Clamp(RewardCost, 1, 10_000_000);
        if (string.IsNullOrWhiteSpace(RewardTitle)) RewardTitle = "🔊 הקראה";
        RewardTitle = RewardTitle.Trim();
        if (RewardTitle.Length > 50) RewardTitle = RewardTitle[..50];
        RewardDescription = (RewardDescription ?? "").Trim();
        if (RewardDescription.Length > 200) RewardDescription = RewardDescription[..200];
        if (!TtsVoices.All.Any(v => v.Id == Voice)) Voice = TtsVoices.Hila;
        RatePercent = Math.Clamp(RatePercent, -50, 50);
        VolumePercent = Math.Clamp(VolumePercent, 0, 100);
        if (string.IsNullOrWhiteSpace(Template) || !Template.Contains("{text}")) Template = "{user} אומר: {text}";
        MaxChars = Math.Clamp(MaxChars, 10, 400);
        MaxAgeMinutes = Math.Clamp(MaxAgeMinutes, 1, 1440);
        GapSeconds = Math.Clamp(GapSeconds, 0, 60);
        BannedWords = (BannedWords ?? new()).Select(w => w.Trim()).Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed record TtsVoice(string Id, string DisplayName, bool IsCloud);

public static class TtsVoices
{
    public const string Hila = "he-IL-HilaNeural";
    public const string Avri = "he-IL-AvriNeural";
    public const string Windows = "windows";

    public static readonly IReadOnlyList<TtsVoice> All = new[]
    {
        new TtsVoice(Hila, "Hila – אישה (איכותי)", true),
        new TtsVoice(Avri, "Avri – גבר (איכותי)", true),
        new TtsVoice(Windows, "Asaf – הקול של Windows", false),
    };

    public static TtsVoice Get(string id) => All.FirstOrDefault(v => v.Id == id) ?? All[0];
}

public enum TtsStatus
{
    /// <summary>ממתין בתור.</summary>
    Waiting,
    /// <summary>מחכה שהסטרימר יאשר (מצב אישור ידני).</summary>
    AwaitingApproval,
    Playing,
    Done,
    Skipped,
    /// <summary>נדחה – הנקודות הוחזרו לצופה.</summary>
    Rejected,
    Failed,
}

public sealed class TtsItem
{
    public required string Id { get; init; }
    public required string User { get; set; }
    public required string Text { get; init; }
    public long UserId { get; init; }
    public DateTime ReceivedAtUtc { get; init; }
    public bool IsTest { get; init; }
    public TtsStatus Status { get; set; }
    public string? Note { get; set; }

    public string StatusText => Status switch
    {
        TtsStatus.Waiting => "בתור",
        TtsStatus.AwaitingApproval => "ממתין לאישור",
        TtsStatus.Playing => "🔊 מוקרא עכשיו",
        TtsStatus.Done => "הוקרא",
        TtsStatus.Skipped => "דולג",
        TtsStatus.Rejected => "נדחה – הנקודות הוחזרו",
        TtsStatus.Failed => "נכשל",
        _ => "",
    };

    public bool IsOpen => Status is TtsStatus.Waiting or TtsStatus.AwaitingApproval;
}

public sealed record TtsRequest(string Text, string Voice, int RatePercent);

public sealed record TtsAudio(byte[] Data, string ContentType)
{
    public string Extension => ContentType.Contains("wav", StringComparison.OrdinalIgnoreCase) ? ".wav" : ".mp3";

    /// <summary>הערכת משך (לפי 48kbps ל-MP3 או 22kHz/16bit ל-WAV) – לזמן המתנה מקסימלי.</summary>
    public TimeSpan EstimatedDuration => TimeSpan.FromSeconds(
        Extension == ".wav" ? Data.Length / 44_100.0 : Data.Length / 6_000.0);
}

public sealed class TtsException : Exception
{
    public TtsException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface ITtsEngine
{
    Task<TtsAudio> SynthesizeAsync(TtsRequest request, CancellationToken ct);
}

public interface ITtsPlayer
{
    /// <summary>משמיע עד הסוף (או עד ביטול = דילוג).</summary>
    Task PlayAsync(TtsAudio audio, int volumePercent, CancellationToken ct);
}

/// <summary>טעינה ושמירה של הגדרות ההקראה.</summary>
public sealed class TtsSettingsStore
{
    private readonly object _lock = new();
    private TtsSettings _current;
    public string FilePath { get; }
    public event Action<TtsSettings>? Changed;

    public TtsSettingsStore(string dataDirectory, ILoggingService? log = null)
    {
        FilePath = Path.Combine(dataDirectory, "tts_settings.json");
        try
        {
            _current = JsonStore.Load<TtsSettings>(FilePath) ?? new TtsSettings();
        }
        catch (Exception ex)
        {
            JsonStore.BackupCorrupt(FilePath);
            log?.Log(Models.LogLevel.Warning, "קובץ הגדרות ההקראה פגום – נטענו ברירות מחדל", ex.Message);
            _current = new TtsSettings();
        }
        _current.Sanitize();
    }

    public TtsSettings Current { get { lock (_lock) return _current.Clone(); } }

    public void Save(TtsSettings s)
    {
        var copy = s.Clone();
        copy.Sanitize();
        lock (_lock)
        {
            _current = copy;
            try { JsonStore.Save(FilePath, copy); } catch { /* נשמר בפעם הבאה */ }
        }
        Changed?.Invoke(copy.Clone());
    }

    public void Update(Action<TtsSettings> change)
    {
        var s = Current;
        change(s);
        Save(s);
    }
}
