using AutoCategory.Core.Models;

namespace AutoCategory.Core.Services;

/// <summary>
/// לולאת הזיהוי: כל שנייה מעדכנת את המנוע (בשביל ספירה לאחור חלקה),
/// וכל "מרווח זיהוי" (ברירת מחדל 2 שניות) קוראת מחדש את החלון הפעיל.
/// רצה ברקע, בלי שום תלות בממשק – הממשק רק מאזין לאירועים.
/// </summary>
public sealed class DetectionCoordinator : IDisposable
{
    private readonly IForegroundWindowService _realProvider;
    private readonly FakeForegroundWindowService _fakeProvider;
    private readonly IGameDetectionService _detector;
    private readonly CategoryEngine _engine;
    private readonly ILoggingService _log;
    private readonly ISettingsService _settings;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DetectionResult? _lastResult;
    private DateTime _lastDetectAt = DateTime.MinValue;
    private string? _lastLoggedExe;
    private volatile bool _testMode;
    private volatile bool _forceDetect;

    public event Action<DetectionResult>? DetectionUpdated;
    public event Action<Exception>? Error;

    public DetectionCoordinator(IForegroundWindowService realProvider, FakeForegroundWindowService fakeProvider,
        IGameDetectionService detector, CategoryEngine engine, ILoggingService log, ISettingsService settings)
    {
        _realProvider = realProvider;
        _fakeProvider = fakeProvider;
        _detector = detector;
        _engine = engine;
        _log = log;
        _settings = settings;
    }

    public CategoryEngine Engine => _engine;
    public FakeForegroundWindowService FakeProvider => _fakeProvider;
    public DetectionResult? LastResult => _lastResult;

    /// <summary>התוצאה האחרונה שהוצגה למשתמש (לא כולל החלון של התוכנה עצמה).</summary>
    public DetectionResult? LastDisplayed => _lastDisplayed;
    private DetectionResult? _lastDisplayed;

    /// <summary>מסד הכללים – נדרש לזיהוי אוטומטי של משחקים חדשים.</summary>
    public ICategoryMappingService? Mappings { get; set; }

    /// <summary>נוסף אוטומטית כלל למשחק חדש (שם המשחק, הספרייה).</summary>
    public event Action<GameMapping, string>? GameAutoAdded;

    private bool TryAutoAddGame(ForegroundInfo fg, AppSettings s)
    {
        var match = GameLibraryDetector.Match(fg.ExecutablePath, fg.WindowTitle, s.ExtraGameFolders);
        if (match is null) return false;
        var mapping = new GameMapping
        {
            Executable = fg.ExecutableName,
            Category = match.GameName,
            Notes = $"נוסף אוטומטית ({match.Library}: {match.FolderName})",
        };
        try
        {
            Mappings!.AddOrUpdate(mapping);
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Warning, "הוספת כלל אוטומטית נכשלה", ex.Message);
            return false;
        }
        _log.Log(LogLevel.Detected, $"זוהה משחק חדש: {match.GameName} ({match.Library}) – נוסף כלל אוטומטית",
            fg.ExecutablePath);
        GameAutoAdded?.Invoke(mapping, match.Library);
        return true;
    }

    /// <summary>שם קובץ ההרצה של התוכנה עצמה.</summary>
    public string SelfExecutable { get; set; } = "BengoAutoCategory.exe";

    public bool TestMode
    {
        get => _testMode;
        set
        {
            if (_testMode == value) return;
            _testMode = value;
            _lastLoggedExe = null;
            _log.Log(LogLevel.Info, value ? "מצב בדיקה הופעל – הזיהוי משתמש בתוכנות מדומות" : "מצב בדיקה כובה – חזרה לזיהוי אמיתי");
            RequestImmediateDetection();
        }
    }

    /// <summary>מבקש זיהוי מיידי בסיבוב הבא (למשל אחרי הוספת כלל).</summary>
    public void RequestImmediateDetection() => _forceDetect = true;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
        _log.Log(LogLevel.Info, $"הזיהוי התחיל (שירות שינוי קטגוריה: {_engine.ChangeServiceName})");
    }

    /// <summary>סיבוב אחד – ציבורי כדי לאפשר בדיקות אוטומטיות.</summary>
    public async Task TickAsync(DateTime now)
    {
        var s = _settings.Current;
        if (_forceDetect || _lastResult is null || now - _lastDetectAt >= TimeSpan.FromSeconds(s.DetectionIntervalSeconds))
        {
            _forceDetect = false;
            _lastDetectAt = now;
            var provider = _testMode ? _fakeProvider : _realProvider;
            var fg = provider.GetForeground();
            var running = provider.GetRunningExecutables();
            _lastResult = _detector.Detect(fg, running);

            // משחק חדש שמותקן בתיקיית משחקים (Steam, Epic...) – מוסיפים לו כלל אוטומטית
            if (_lastResult.Kind == DetectionKind.Unknown && s.AutoDetectGames && Mappings is not null &&
                TryAutoAddGame(fg, s))
                _lastResult = _detector.Detect(fg, running);

            // החלון של התוכנה עצמה: למנוע הוא "שקוף" (כמו תוכנה מותעלמת),
            // אבל בתצוגה ממשיכים להראות את התוכנה הקודמת – אחרת לוח הבקרה תמיד מציג את עצמו.
            var isSelf = string.Equals(fg.ExecutableName, SelfExecutable, StringComparison.OrdinalIgnoreCase);
            if (!isSelf || _lastDisplayed is null)
            {
                _lastDisplayed = _lastResult;
                LogIfChanged(_lastResult);
                DetectionUpdated?.Invoke(_lastResult);
            }
        }

        if (!_engine.IsPaused)
            await _engine.UpdateAsync(_lastResult, now).ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync(DateTime.Now).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
                _log.Log(LogLevel.Warning, "שגיאה בלולאת הזיהוי", ex.Message);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void LogIfChanged(DetectionResult r)
    {
        var exe = r.Foreground.ExecutableName;
        if (string.Equals(exe, _lastLoggedExe, StringComparison.OrdinalIgnoreCase)) return;
        _lastLoggedExe = exe;
        if (string.IsNullOrEmpty(exe)) return;

        switch (r.Kind)
        {
            case DetectionKind.Game:
                _log.Log(LogLevel.Detected, $"זוהה {exe} ({r.Category}, ודאות {r.Confidence}%)", r.Foreground.WindowTitle);
                break;
            case DetectionKind.Ignored:
                _log.Log(LogLevel.Ignored, $"זוהה {exe} – ברשימת ההתעלמות", r.Foreground.WindowTitle);
                break;
            case DetectionKind.Unknown:
                _log.Log(LogLevel.Detected, $"זוהתה תוכנה לא מוכרת: {exe}", r.Foreground.WindowTitle);
                break;
        }
    }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(3)); } catch { /* ignore */ }
        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    public void Dispose() => Stop();
}
