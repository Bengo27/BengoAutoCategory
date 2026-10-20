using AutoCategory.Core.Models;
using AutoCategory.Core.Platforms.Kick;
using AutoCategory.Core.Services;

namespace AutoCategory.Core.Tts;

/// <summary>
/// הקראת הודעות של צופים שקנו את פרס ההקראה בנקודות הערוץ:
/// מושך בקשות מקיק → מסנן → תור → מקריא → מאשר בקיק (או דוחה ומחזיר נקודות).
/// </summary>
public sealed class TtsService : IDisposable
{
    public const string RewardColor = "#2DD4BF";
    private const int KeepFinished = 40;

    private readonly IKickRewardsApi _kick;
    private readonly ITtsEngine _engine;
    private readonly ITtsPlayer _appPlayer;
    private readonly TtsWidgetHub _widget;
    private readonly TtsSettingsStore _settings;
    private readonly ILoggingService _log;
    private readonly Func<DateTime> _utcNow;

    private readonly object _lock = new();
    private readonly List<TtsItem> _items = new();
    private readonly HashSet<string> _seen = new();
    private readonly Dictionary<long, string> _names = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private CancellationTokenSource? _currentPlayback;
    private CancellationTokenSource? _loops;
    private bool _paused;
    private string? _lastPollError;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>התור או הסטטוס השתנו (לעדכון המסך).</summary>
    public event Action? Changed;

    public TtsService(IKickRewardsApi kick, ITtsEngine engine, ITtsPlayer appPlayer, TtsWidgetHub widget,
        TtsSettingsStore settings, ILoggingService log, Func<DateTime>? utcNow = null)
    {
        _kick = kick;
        _engine = engine;
        _appPlayer = appPlayer;
        _widget = widget;
        _settings = settings;
        _log = log;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public TtsSettingsStore Settings => _settings;

    public IReadOnlyList<TtsItem> Items { get { lock (_lock) return _items.ToList(); } }

    public TtsItem? Current { get { lock (_lock) return _items.FirstOrDefault(i => i.Status == TtsStatus.Playing); } }

    /// <summary>השהיית התור: הבקשות ממשיכות להגיע אבל לא מוקראות.</summary>
    public bool QueuePaused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (!value) _wake.Release();
            Changed?.Invoke();
        }
    }

    /// <summary>שגיאה אחרונה מול קיק (null = הכול תקין).</summary>
    public string? LastError => _lastPollError;

    // ───────────────────────── הפעלה ─────────────────────────

    public void Start()
    {
        if (_loops is not null) return;
        _loops = new CancellationTokenSource();
        var ct = _loops.Token;
        _ = Task.Run(() => PollLoopAsync(ct));
        _ = Task.Run(() => PlayLoopAsync(ct));
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await PollOnceAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { /* נרשם ב-PollOnceAsync */ }
            try { await Task.Delay(PollInterval, ct).ConfigureAwait(false); } catch { break; }
        }
    }

    private async Task PlayLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool played;
            try { played = await PlayNextAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.Log(LogLevel.Warning, "שגיאה בהקראה", ex.Message);
                played = false;
            }
            if (played)
            {
                var gap = _settings.Current.GapSeconds;
                if (gap > 0) try { await Task.Delay(TimeSpan.FromSeconds(gap), ct).ConfigureAwait(false); } catch { break; }
                continue;
            }
            try { await _wake.WaitAsync(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false); } catch { break; }
        }
    }

    // ───────────────────────── משיכת בקשות מקיק ─────────────────────────

    /// <summary>בודק בקשות חדשות בקיק (פעם אחת). מוחזר מספר הבקשות החדשות.</summary>
    public async Task<int> PollOnceAsync(CancellationToken ct)
    {
        var s = _settings.Current;
        if (!s.Enabled || string.IsNullOrEmpty(s.RewardId) || !_kick.IsConnected) return 0;
        if (!await _pollGate.WaitAsync(0, ct).ConfigureAwait(false)) return 0;
        try
        {
            IReadOnlyList<KickRedemption> pending;
            try
            {
                pending = await _kick.GetPendingRedemptionsAsync(s.RewardId, ct).ConfigureAwait(false);
            }
            catch (KickException ex)
            {
                SetError(ex.StatusCode == 404
                    ? "פרס ההקראה לא נמצא בקיק (אולי נמחק). לחץ \"צור / עדכן פרס בקיק\"."
                    : ex.Message);
                return 0;
            }
            catch (HttpRequestException ex)
            {
                SetError("אין חיבור לקיק: " + ex.Message);
                return 0;
            }
            SetError(null);

            List<KickRedemption> fresh;
            lock (_lock) fresh = pending.Where(p => !_seen.Contains(p.Id)).OrderBy(p => p.RedeemedAtUtc).ToList();
            if (fresh.Count == 0) return 0;

            await LoadNamesAsync(fresh.Select(f => f.UserId), ct).ConfigureAwait(false);

            var toReject = new List<(TtsItem Item, string Reason)>();
            lock (_lock)
            {
                foreach (var r in fresh)
                {
                    _seen.Add(r.Id);
                    var user = _names.TryGetValue(r.UserId, out var n) ? n : "צופה";
                    var check = TtsFilter.Check(r.UserInput, s);
                    var item = new TtsItem
                    {
                        Id = r.Id,
                        User = user,
                        UserId = r.UserId,
                        Text = check.Allowed ? check.Text : (r.UserInput ?? "").Trim(),
                        ReceivedAtUtc = r.RedeemedAtUtc,
                    };

                    if (_utcNow() - r.RedeemedAtUtc > TimeSpan.FromMinutes(s.MaxAgeMinutes))
                        toReject.Add((item, "בקשה ישנה (התוכנה לא פעלה בזמן הקנייה)"));
                    else if (!check.Allowed)
                        toReject.Add((item, check.Reason ?? "נחסם"));
                    else
                        item.Status = s.ManualApproval ? TtsStatus.AwaitingApproval : TtsStatus.Waiting;

                    _items.Add(item);
                }
            }

            foreach (var (item, reason) in toReject)
                await RejectCoreAsync(item, reason, ct).ConfigureAwait(false);

            foreach (var r in fresh.Where(f => toReject.All(t => t.Item.Id != f.Id)))
            {
                var item = Find(r.Id);
                if (item is not null)
                    _log.Log(LogLevel.Info, $"🔊 בקשת הקראה מ-{item.User}" + (s.ManualApproval ? " (ממתינה לאישור)" : ""), item.Text);
            }

            Trim();
            Changed?.Invoke();
            _wake.Release();
            return fresh.Count;
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private async Task LoadNamesAsync(IEnumerable<long> userIds, CancellationToken ct)
    {
        List<long> missing;
        lock (_lock) missing = userIds.Where(id => id > 0 && !_names.ContainsKey(id)).Distinct().ToList();
        if (missing.Count == 0) return;
        try
        {
            var found = await _kick.GetUserNamesAsync(missing, ct).ConfigureAwait(false);
            lock (_lock) foreach (var (id, name) in found) _names[id] = name;
        }
        catch (Exception ex) when (ex is KickException or HttpRequestException)
        {
            // בלי שם – "צופה"
        }
    }

    private void SetError(string? message)
    {
        if (message == _lastPollError) return;
        _lastPollError = message;
        if (message is not null) _log.Log(LogLevel.Warning, "הקראה: " + message);
        Changed?.Invoke();
    }

    // ───────────────────────── הקראה ─────────────────────────

    /// <summary>מקריא את הבקשה הבאה בתור. false = אין מה להקריא.</summary>
    public async Task<bool> PlayNextAsync(CancellationToken ct)
    {
        var enabled = _settings.Current.Enabled;
        TtsItem? item;
        lock (_lock)
        {
            // בדיקת קול עובדת תמיד; הודעות של צופים – רק כשההקראה פעילה והתור לא מושהה
            item = _items.FirstOrDefault(i => i.Status == TtsStatus.Waiting && (i.IsTest || (enabled && !_paused)));
            if (item is null) return false;
            item.Status = TtsStatus.Playing;
        }
        Changed?.Invoke();

        var s = _settings.Current;
        using var playback = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_lock) _currentPlayback = playback;
        var skipped = false;
        try
        {
            var spoken = TtsFilter.ApplyTemplate(s.Template, item.User, item.Text);
            var audio = await _engine.SynthesizeAsync(new TtsRequest(spoken, s.Voice, s.RatePercent), playback.Token)
                .ConfigureAwait(false);

            var inWidget = s.Playback == TtsPlayback.Widget && _widget.HasClient;
            if (s.Playback == TtsPlayback.Widget && !inWidget)
                _log.Log(LogLevel.Warning, "הווידג'ט לא פתוח ב-OBS – ההקראה מושמעת במחשב");

            await _widget.ShowAsync(item, audio, inWidget, s.VolumePercent, playback.Token).ConfigureAwait(false);
            if (!inWidget) await _appPlayer.PlayAsync(audio, s.VolumePercent, playback.Token).ConfigureAwait(false);
            item.Status = TtsStatus.Done;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            skipped = true;
            item.Status = TtsStatus.Skipped;
        }
        catch (TtsException ex)
        {
            item.Status = TtsStatus.Failed;
            item.Note = ex.Message;
            _log.Log(LogLevel.Warning, $"ההקראה של {item.User} נכשלה", ex.Message);
        }
        finally
        {
            _widget.Clear();
            lock (_lock) _currentPlayback = null;
        }

        if (!item.IsTest)
        {
            // הוקרא או דולג → הנקודות נוצלו. נכשל → מחזירים נקודות.
            if (item.Status == TtsStatus.Failed) await RejectCoreAsync(item, "ההקראה נכשלה", ct, keepStatus: true).ConfigureAwait(false);
            else await AcceptCoreAsync(item, ct).ConfigureAwait(false);
        }
        if (skipped) _log.Log(LogLevel.Info, $"דילגת על ההקראה של {item.User}");

        Trim();
        Changed?.Invoke();
        return true;
    }

    /// <summary>עוצר את ההקראה הנוכחית.</summary>
    public void SkipCurrent()
    {
        lock (_lock) _currentPlayback?.Cancel();
    }

    /// <summary>מאשר בקשה שממתינה לאישור (מצב ידני).</summary>
    public void Approve(TtsItem item)
    {
        lock (_lock)
        {
            if (item.Status != TtsStatus.AwaitingApproval) return;
            item.Status = TtsStatus.Waiting;
        }
        Changed?.Invoke();
        _wake.Release();
    }

    /// <summary>דוחה בקשה ומחזיר לצופה את הנקודות.</summary>
    public async Task RejectAsync(TtsItem item, CancellationToken ct = default)
    {
        if (!item.IsOpen) return;
        await RejectCoreAsync(item, "נדחה על ידי הסטרימר", ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>בדיקת קול (בלי קיק): מכניס הודעה לראש התור.</summary>
    public void EnqueueTest(string text)
    {
        var check = TtsFilter.Check(text, new TtsSettings { BlockLinks = false, BannedWords = new(), MaxChars = 400 });
        lock (_lock)
        {
            _items.Insert(0, new TtsItem
            {
                Id = "test-" + Guid.NewGuid().ToString("N"),
                User = "בדיקה",
                Text = check.Text.Length > 0 ? check.Text : "בדיקת קול",
                ReceivedAtUtc = _utcNow(),
                IsTest = true,
                Status = TtsStatus.Waiting,
            });
        }
        Changed?.Invoke();
        _wake.Release();
    }

    private async Task AcceptCoreAsync(TtsItem item, CancellationToken ct)
    {
        try { await _kick.AcceptRedemptionsAsync(new[] { item.Id }, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is KickException or HttpRequestException)
        {
            _log.Log(LogLevel.Warning, "לא ניתן לסמן בקשת הקראה כמאושרת בקיק", ex.Message);
        }
    }

    private async Task RejectCoreAsync(TtsItem item, string reason, CancellationToken ct, bool keepStatus = false)
    {
        if (!keepStatus)
        {
            item.Status = TtsStatus.Rejected;
            item.Note = reason;
        }
        if (item.IsTest) return;
        try
        {
            await _kick.RejectRedemptionsAsync(new[] { item.Id }, ct).ConfigureAwait(false);
            _log.Log(LogLevel.Info, $"בקשת ההקראה של {item.User} נדחתה – הנקודות הוחזרו", reason);
        }
        catch (Exception ex) when (ex is KickException or HttpRequestException)
        {
            _log.Log(LogLevel.Warning, "לא ניתן לדחות את בקשת ההקראה בקיק", ex.Message);
        }
    }

    private TtsItem? Find(string id)
    {
        lock (_lock) return _items.FirstOrDefault(i => i.Id == id);
    }

    /// <summary>שומר רק את ההיסטוריה האחרונה.</summary>
    private void Trim()
    {
        lock (_lock)
        {
            var finished = _items.Where(i => !i.IsOpen && i.Status != TtsStatus.Playing).ToList();
            foreach (var old in finished.Take(Math.Max(0, finished.Count - KeepFinished))) _items.Remove(old);
        }
    }

    // ───────────────────────── הפרס בקיק ─────────────────────────

    /// <summary>
    /// יוצר את פרס ההקראה בקיק, או מעדכן אותו (שם, מחיר, תיאור) אם כבר קיים.
    /// </summary>
    public async Task<KickReward> EnsureRewardAsync(CancellationToken ct = default)
    {
        if (!_kick.IsConnected) throw new KickException("קודם צריך להתחבר לקיק.");
        if (!_kick.HasRewardsScope)
            throw new KickException("החיבור לקיק נעשה לפני שהוספנו את ההקראה. בעמוד \"חיבור לקיק\": התנתק ← התחבר שוב, ואשר את ההרשאות החדשות.", 403);

        var s = _settings.Current;
        KickReward reward;
        try
        {
            if (!string.IsNullOrEmpty(s.RewardId))
            {
                try
                {
                    reward = await _kick.UpdateRewardAsync(s.RewardId, new Dictionary<string, object>
                    {
                        ["title"] = s.RewardTitle,
                        ["cost"] = s.RewardCost,
                        ["description"] = s.RewardDescription,
                        ["is_user_input_required"] = true,
                        ["is_enabled"] = true,
                        ["is_paused"] = !s.Enabled,
                    }, ct).ConfigureAwait(false);
                    _log.Log(LogLevel.Info, $"פרס ההקראה עודכן בקיק: {reward.Title} ({reward.Cost} נקודות)");
                    return reward;
                }
                catch (KickException ex) when (ex.StatusCode == 404)
                {
                    // נמחק בקיק – ניצור חדש
                }
            }

            reward = await _kick.CreateRewardAsync(s.RewardTitle, s.RewardCost, s.RewardDescription, RewardColor, ct).ConfigureAwait(false);
            _settings.Update(x => x.RewardId = reward.Id);
            if (!s.Enabled)
                await _kick.UpdateRewardAsync(reward.Id, new Dictionary<string, object> { ["is_paused"] = true }, ct).ConfigureAwait(false);
            _log.Log(LogLevel.Info, $"נוצר פרס הקראה בקיק: {reward.Title} ({reward.Cost} נקודות)");
            return reward;
        }
        catch (KickException ex) when (ex.StatusCode == 403)
        {
            throw new KickException("קיק לא מאשר לנהל פרסים. בעמוד \"חיבור לקיק\": התנתק ← התחבר שוב, ואשר את ההרשאות החדשות.", 403, ex);
        }
    }

    /// <summary>
    /// הפעלה / כיבוי של ההקראה. כיבוי משהה את הפרס בקיק (צופים לא יכולים לקנות),
    /// הפעלה מחזירה אותו.
    /// </summary>
    public async Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        _settings.Update(s => s.Enabled = enabled);
        var id = _settings.Current.RewardId;
        if (!string.IsNullOrEmpty(id) && _kick.IsConnected)
        {
            try
            {
                await _kick.UpdateRewardAsync(id, new Dictionary<string, object> { ["is_paused"] = !enabled }, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is KickException or HttpRequestException)
            {
                _log.Log(LogLevel.Warning, enabled ? "לא ניתן להפעיל את פרס ההקראה בקיק" : "לא ניתן להשהות את פרס ההקראה בקיק", ex.Message);
            }
        }
        _log.Log(LogLevel.Info, enabled ? "🔊 ההקראה הופעלה" : "ההקראה כובתה (הפרס בקיק מושהה)");
        if (!enabled) SkipCurrent();
        Changed?.Invoke();
        _wake.Release();
    }

    public void Dispose()
    {
        _loops?.Cancel();
        SkipCurrent();
    }
}
