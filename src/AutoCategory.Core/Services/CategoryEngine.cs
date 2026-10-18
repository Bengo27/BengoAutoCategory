using AutoCategory.Core.Models;

namespace AutoCategory.Core.Services;

public enum EnginePhase
{
    /// <summary>הכול תקין – הקטגוריה תואמת למה שמשחקים (או שאין מה לשנות).</summary>
    Idle,
    /// <summary>זוהה משחק חדש – ספירה לאחור עד שינוי.</summary>
    Pending,
    /// <summary>יש שינוי ממתין, אבל המשחק לא בחזית כרגע (Alt-Tab לתוכנה מותעלמת) – הספירה מושהית.</summary>
    PendingPaused,
    /// <summary>הזיהוי אומת אבל מצב אוטומטי כבוי – מחכה ללחיצה על "שנה עכשיו".</summary>
    AwaitingManual,
    /// <summary>הזיהוי אומת אבל עוד לא עבר הזמן המינימלי מהשינוי הקודם.</summary>
    Cooldown,
    /// <summary>אין משחק פעיל – ספירה לאחור לחזרה לקטגוריית ברירת המחדל.</summary>
    ReturningToDefault,
    /// <summary>הזיהוי מושהה.</summary>
    Paused,
}

/// <summary>תמונת מצב של המנוע לתצוגה בממשק.</summary>
public sealed record EngineState(
    EnginePhase Phase,
    string CurrentCategory,
    string? PendingCategory,
    int SecondsRemaining,
    bool AutoEnabled,
    string StatusText,
    DetectionResult? LastDetection);

/// <summary>
/// מנוע הקטגוריות – "המוח" של התוכנה.
///
/// כללי בטיחות:
///  • משחק חדש חייב להיות בחזית במשך זמן האימות (ברירת מחדל 20 שניות) לפני שינוי.
///  • תוכנות מרשימת ההתעלמות ותוכנות לא מוכרות "שקופות": הן לא מבטלות ולא מקדמות את הספירה (היא מושהית).
///  • חזרה למשחק של הקטגוריה הנוכחית מבטלת שינוי ממתין (Alt-Tab קצר למשחק אחר לא ישנה כלום).
///  • משחק אחר שמזוהה מחליף את השינוי הממתין ומתחיל ספירה מחדש.
///  • לעולם לא "משנים" לקטגוריה שכבר נבחרה.
///  • הגבלת קצב: זמן מינימלי בין שינויים אוטומטיים.
///  • משחק שרץ ברקע מונע חזרה ל-Just Chatting, אבל לעולם לא מתחיל שינוי בעצמו.
/// </summary>
public sealed class CategoryEngine
{
    private readonly ICategoryChangeService _changer;
    private readonly ILoggingService _log;
    private readonly Func<AppSettings> _settings;
    private readonly Action<string>? _persistCurrentCategory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>פער מקסימלי שנספר בין שתי בדיקות (מגן מקפיצות אחרי שינה/הקפאה של המחשב).</summary>
    public TimeSpan MaxTickGap { get; set; } = TimeSpan.FromSeconds(5);

    private string _current;
    private string? _pending;
    private GameMapping? _pendingMapping;
    private TimeSpan _pendingElapsed;
    private bool _pendingConfirmed;
    private string? _dismissed;
    private DateTime? _lastUpdate;
    private DateTime? _lastChangeAt;
    private DateTime? _noGameSince;
    private bool _paused;
    private DetectionResult? _lastDetection;
    private DetectionResult? _lastGameDetection;
    private EngineState _state;

    public event Action<EngineState>? StateChanged;
    /// <summary>התראה למשתמש (כותרת, טקסט).</summary>
    public event Action<string, string>? Notify;

    public CategoryEngine(ICategoryChangeService changer, ILoggingService log, Func<AppSettings> settings,
        string initialCategory, Action<string>? persistCurrentCategory = null)
    {
        _changer = changer;
        _log = log;
        _settings = settings;
        _persistCurrentCategory = persistCurrentCategory;
        _current = string.IsNullOrWhiteSpace(initialCategory) ? settings().DefaultCategory : initialCategory;
        _state = new EngineState(EnginePhase.Idle, _current, null, 0, settings().AutoCategoryEnabled, "ממתין לזיהוי…", null);
    }

    public EngineState State => _state;
    public string CurrentCategory => _current;
    public string ChangeServiceName => _changer.DisplayName;

    // ───────────────────────────── עדכון תקופתי ─────────────────────────────

    public async Task UpdateAsync(DetectionResult detection, DateTime now)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await UpdateCoreAsync(detection, now).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpdateCoreAsync(DetectionResult detection, DateTime now)
    {
        var s = _settings();
        var dt = _lastUpdate is null ? TimeSpan.Zero : now - _lastUpdate.Value;
        if (dt < TimeSpan.Zero) dt = TimeSpan.Zero;
        if (dt > MaxTickGap) dt = MaxTickGap;
        _lastUpdate = now;
        _lastDetection = detection;
        if (detection.Kind == DetectionKind.Game) _lastGameDetection = detection;

        if (_paused)
        {
            Publish(EnginePhase.Paused, "הזיהוי מושהה", s);
            return;
        }

        var fgCategory = detection.Kind == DetectionKind.Game ? detection.Category : null;

        // ביטול ידני נשאר בתוקף עד שבחזית מופיע משחק אחר
        if (fgCategory is not null && !Eq(fgCategory, _dismissed)) _dismissed = null;

        // מעקב "אין משחק" לצורך חזרה לקטגוריית ברירת מחדל
        if (detection.AnyGameRunning) _noGameSince = null;
        else _noGameSince ??= now;

        if (fgCategory is not null)
        {
            if (Eq(fgCategory, _current))
            {
                if (_pending is not null)
                {
                    _log.Log(LogLevel.Cancelled, $"חזרת ל-{_current} – השינוי ל-{_pending} בוטל");
                    ClearPending();
                }
            }
            else if (Eq(fgCategory, _dismissed))
            {
                // המשתמש ביטל את ההצעה הזו – לא מציעים שוב עד שיחליף משחק
            }
            else if (_pending is null || !Eq(_pending, fgCategory))
            {
                if (_pending is not null)
                    _log.Log(LogLevel.Cancelled, $"זוהה משחק אחר – השינוי ל-{_pending} בוטל");
                StartPending(fgCategory, detection.Mapping, s);
            }
            else
            {
                _pendingElapsed += dt;
            }
        }
        // אחרת: תוכנה מותעלמת / לא מוכרת / אין חלון → "שקוף", הספירה מושהית

        // ── שינוי ממתין ──
        if (_pending is not null)
        {
            var delay = TimeSpan.FromSeconds(s.VerificationDelaySeconds);
            if (!_pendingConfirmed && _pendingElapsed >= delay)
            {
                _pendingConfirmed = true;
                _log.Log(LogLevel.Confirmed, $"הזיהוי אומת: {_pending}");
            }

            if (!_pendingConfirmed)
            {
                var remaining = Ceil(delay - _pendingElapsed);
                var inForeground = Eq(fgCategory, _pending);
                Publish(inForeground ? EnginePhase.Pending : EnginePhase.PendingPaused,
                    inForeground
                        ? $"זוהה {_pending} – שינוי קטגוריה בעוד {remaining} שניות"
                        : $"השינוי ל-{_pending} מושהה – המשחק לא בחזית ({remaining} שניות נותרו)",
                    s, remaining);
                return;
            }

            if (!s.AutoCategoryEnabled)
            {
                Publish(EnginePhase.AwaitingManual, $"מוצע לשנות ל-{_pending} (מצב אוטומטי כבוי)", s);
                return;
            }

            var cooldown = CooldownRemaining(now, s);
            if (cooldown > 0)
            {
                Publish(EnginePhase.Cooldown, $"מחכה {cooldown} שניות (זמן מינימלי בין שינויים) לפני מעבר ל-{_pending}", s, cooldown);
                return;
            }

            await ExecuteChangeAsync(_pending, _pendingMapping, now, manual: false).ConfigureAwait(false);
            Publish(EnginePhase.Idle, IdleText(detection), s);
            return;
        }

        // ── חזרה לקטגוריית ברירת מחדל ──
        if (s.ReturnToDefaultEnabled && _noGameSince is not null && !Eq(_current, s.DefaultCategory))
        {
            var remaining = Ceil(TimeSpan.FromMinutes(s.ReturnToDefaultMinutes) - (now - _noGameSince.Value));
            if (remaining > 0)
            {
                Publish(EnginePhase.ReturningToDefault, $"אין משחק פעיל – חזרה ל-{s.DefaultCategory} בעוד {FormatSeconds(remaining)}", s, remaining);
                return;
            }

            if (!s.AutoCategoryEnabled)
            {
                Publish(EnginePhase.AwaitingManual, $"מוצע לחזור ל-{s.DefaultCategory} (מצב אוטומטי כבוי)", s);
                return;
            }

            var cooldown = CooldownRemaining(now, s);
            if (cooldown > 0)
            {
                Publish(EnginePhase.Cooldown, $"מחכה {cooldown} שניות לפני חזרה ל-{s.DefaultCategory}", s, cooldown);
                return;
            }

            _log.Log(LogLevel.Info, $"אין משחק פעיל {s.ReturnToDefaultMinutes} דקות – חוזרים ל-{s.DefaultCategory}");
            await ExecuteChangeAsync(s.DefaultCategory, null, now, manual: false).ConfigureAwait(false);
        }

        Publish(EnginePhase.Idle, IdleText(detection), s);
    }

    // ───────────────────────────── פעולות משתמש ─────────────────────────────

    /// <summary>"שנה עכשיו": מבצע מיד את השינוי המוצע (בלי לחכות לטיימר או להגבלת הקצב).</summary>
    public async Task<bool> ChangeNowAsync(DateTime now)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var s = _settings();
            string? target = _pending;
            GameMapping? mapping = _pendingMapping;

            // גם אם כרגע לוח הבקרה בחזית – לוקחים את המשחק האחרון שזוהה
            if (target is null && _lastGameDetection is not null && !Eq(_lastGameDetection.Category, _current))
            {
                target = _lastGameDetection.Category;
                mapping = _lastGameDetection.Mapping;
            }
            if (target is null && _state.Phase is EnginePhase.ReturningToDefault or EnginePhase.AwaitingManual or EnginePhase.Cooldown
                && !Eq(_current, s.DefaultCategory) && _lastDetection is { AnyGameRunning: false })
            {
                target = s.DefaultCategory;
            }

            if (target is null)
            {
                _log.Log(LogLevel.Info, "אין שינוי קטגוריה ממתין");
                return false;
            }

            await ExecuteChangeAsync(target, mapping, now, manual: true).ConfigureAwait(false);
            Publish(EnginePhase.Idle, _lastDetection is null ? "" : IdleText(_lastDetection), s);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>"ביטול": מבטל את השינוי הממתין. לא יוצע שוב עד שתעבור למשחק אחר.</summary>
    public void Cancel(DateTime now)
    {
        _gate.Wait();
        try
        {
            var s = _settings();
            if (_pending is not null)
            {
                _log.Log(LogLevel.Cancelled, $"השינוי ל-{_pending} בוטל ידנית");
                _dismissed = _pending;
                ClearPending();
            }
            else if (_noGameSince is not null && !Eq(_current, s.DefaultCategory))
            {
                _log.Log(LogLevel.Cancelled, $"החזרה ל-{s.DefaultCategory} נדחתה");
                _noGameSince = now; // מתחילים לספור מחדש
            }
            Publish(EnginePhase.Idle, _lastDetection is null ? "בוטל" : IdleText(_lastDetection), s);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>קובע ידנית מה הקטגוריה הנוכחית בסטרים (בלי "לשנות" אותה בפלטפורמה).</summary>
    public void SetCurrentCategoryManually(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        _gate.Wait();
        try
        {
            _current = category.Trim();
            ClearPending();
            _dismissed = null;
            _persistCurrentCategory?.Invoke(_current);
            _log.Log(LogLevel.Info, $"הקטגוריה הנוכחית הוגדרה ידנית: {_current}");
            Publish(EnginePhase.Idle, $"הקטגוריה הנוכחית: {_current}", _settings());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// סנכרון עם הקטגוריה האמיתית בפלטפורמה. אם הסטרימר שינה קטגוריה בעצמו (מחוץ לתוכנה),
    /// מכבדים את זה: מעדכנים את הקטגוריה הנוכחית ולא מציעים שוב את המשחק הנוכחי עד שיחליף משחק.
    /// </summary>
    public bool SyncFromPlatform(string? platformCategory)
    {
        if (string.IsNullOrWhiteSpace(platformCategory)) return false;
        _gate.Wait();
        try
        {
            if (Eq(platformCategory, _current)) return false;

            var previous = _current;
            _current = platformCategory.Trim();
            if (_pending is not null && !Eq(_pending, _current))
                _log.Log(LogLevel.Cancelled, $"השינוי ל-{_pending} בוטל – הקטגוריה שונתה ידנית בפלטפורמה");
            ClearPending();

            // המשחק שבחזית "שייך" לקטגוריה אחרת – הסטרימר בחר במכוון, לא נלחמים בו
            var fgGame = _lastGameDetection?.Category;
            _dismissed = fgGame is not null && !Eq(fgGame, _current) ? fgGame : null;

            _persistCurrentCategory?.Invoke(_current);
            _log.Log(LogLevel.Info, $"הקטגוריה בסטרים שונתה מחוץ לתוכנה: מ-{previous} ל-{_current}");
            Publish(EnginePhase.Idle, $"הקטגוריה הנוכחית: {_current}", _settings());
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>לאחר שינוי שבוצע על ידי התוכנה – לא לפרש את הסנכרון הבא כשינוי ידני.</summary>
    public DateTime? LastChangeAt => _lastChangeAt;

    public void SetPaused(bool paused)
    {
        _gate.Wait();
        try
        {
            if (_paused == paused) return;
            _paused = paused;
            if (paused)
            {
                if (_pending is not null) _log.Log(LogLevel.Cancelled, $"הזיהוי הושהה – השינוי ל-{_pending} בוטל");
                ClearPending();
                _noGameSince = null;
                _log.Log(LogLevel.Info, "הזיהוי הושהה");
                Publish(EnginePhase.Paused, "הזיהוי מושהה", _settings());
            }
            else
            {
                _lastUpdate = null;
                _log.Log(LogLevel.Info, "הזיהוי חודש");
                Publish(EnginePhase.Idle, "ממשיך בזיהוי…", _settings());
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsPaused => _paused;

    // ───────────────────────────── עזר ─────────────────────────────

    private void StartPending(string category, GameMapping? mapping, AppSettings s)
    {
        _pending = category;
        _pendingMapping = mapping;
        _pendingElapsed = TimeSpan.Zero;
        _pendingConfirmed = false;
        _log.Log(LogLevel.Pending, $"זוהה {category} – שינוי קטגוריה מוצע בעוד {s.VerificationDelaySeconds} שניות");
        Notify?.Invoke($"זוהה {category}", $"הקטגוריה תשתנה בעוד {s.VerificationDelaySeconds} שניות אם תמשיך לשחק");
    }

    private void ClearPending()
    {
        _pending = null;
        _pendingMapping = null;
        _pendingElapsed = TimeSpan.Zero;
        _pendingConfirmed = false;
    }

    private async Task ExecuteChangeAsync(string target, GameMapping? mapping, DateTime now, bool manual)
    {
        if (Eq(target, _current))
        {
            _log.Log(LogLevel.Info, $"הקטגוריה כבר {_current} – אין צורך לשנות");
            ClearPending();
            return;
        }

        var from = _current;
        CategoryChangeResult result;
        try
        {
            result = await _changer.ChangeCategoryAsync(from, target, mapping).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new CategoryChangeResult(false, ex.Message);
        }

        _lastChangeAt = now; // גם כישלון נחשב, כדי לא להציף ניסיונות חוזרים

        if (!result.Success)
        {
            _log.Log(LogLevel.Warning, $"שינוי הקטגוריה ל-{target} נכשל", result.Message);
            Notify?.Invoke($"שינוי הקטגוריה ל-{target} נכשל", result.Message);
            return;
        }

        _current = target;
        ClearPending();
        _dismissed = null;
        _persistCurrentCategory?.Invoke(_current);
        Notify?.Invoke("הקטגוריה שונתה", manual ? $"קטגוריה חדשה: {target} (ידני)" : $"קטגוריה חדשה: {target}");
    }

    private int CooldownRemaining(DateTime now, AppSettings s)
    {
        if (_lastChangeAt is null || s.MinSecondsBetweenChanges <= 0) return 0;
        return Math.Max(0, Ceil(TimeSpan.FromSeconds(s.MinSecondsBetweenChanges) - (now - _lastChangeAt.Value)));
    }

    private string IdleText(DetectionResult d) => d.Kind switch
    {
        DetectionKind.Game when Eq(d.Category, _current) => $"משחק ב-{_current} – הקטגוריה מעודכנת",
        DetectionKind.Game when Eq(d.Category, _dismissed) => $"ההצעה ל-{d.Category} בוטלה",
        DetectionKind.Ignored => "תוכנה ברשימת ההתעלמות – אין שינוי",
        DetectionKind.Unknown => "תוכנה לא מוכרת – אפשר להוסיף כלל",
        _ => "ממתין לזיהוי…",
    };

    private void Publish(EnginePhase phase, string text, AppSettings s, int seconds = 0)
    {
        _state = new EngineState(phase, _current, _pending, seconds, s.AutoCategoryEnabled, text, _lastDetection);
        StateChanged?.Invoke(_state);
    }

    private static int Ceil(TimeSpan t) => t <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(t.TotalSeconds - 1e-9);

    private static string FormatSeconds(int seconds) =>
        seconds >= 60 ? $"{seconds / 60}:{seconds % 60:00} דקות" : $"{seconds} שניות";

    private static bool Eq(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
