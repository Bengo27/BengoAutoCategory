using AutoCategory.Core.Models;
using AutoCategory.Core.Services;

namespace AutoCategory.Core.Platforms.Kick;

/// <summary>
/// קורא מדי פעם את הקטגוריה האמיתית בערוץ ומעדכן את המנוע, כדי שהתוכנה תדע
/// אם הסטרימר שינה קטגוריה בעצמו באתר של קיק.
/// </summary>
public sealed class KickSyncService : IDisposable
{
    private readonly KickPlatform _kick;
    private readonly CategoryEngine _engine;
    private readonly ILoggingService _log;
    private CancellationTokenSource? _cts;
    private string? _lastError;
    private bool? _wasLive;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>מותר לשנות בקיק? (false = מצב סימולציה – רק קוראים)</summary>
    public Func<bool> CanWrite { get; set; } = () => true;

    /// <summary>מידע הערוץ האחרון שנקרא (לתצוגה).</summary>
    public KickChannelInfo? LastChannel { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public event Action<KickChannelInfo>? ChannelUpdated;

    public KickSyncService(KickPlatform kick, CategoryEngine engine, ILoggingService log)
    {
        _kick = kick;
        _engine = engine;
        _log = log;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                if (_kick.IsConnected) await SyncNowAsync(ct).ConfigureAwait(false);
                try { await Task.Delay(Interval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }, ct);
    }

    /// <summary>סנכרון אחד. מחזיר true אם הקטגוריה הנוכחית עודכנה מקיק.</summary>
    public async Task<bool> SyncNowAsync(CancellationToken ct = default)
    {
        if (!_kick.IsConnected) return false;
        try
        {
            var channel = await _kick.GetChannelAsync(ct).ConfigureAwait(false);
            LastChannel = channel;
            LastSyncAt = DateTime.Now;
            _lastError = null;
            ChannelUpdated?.Invoke(channel);

            var wasLive = _wasLive;
            _wasLive = channel.IsLive;

            // כשלא בשידור קיק לא מחזיר קטגוריה – אין מה לסנכרן
            if (!channel.IsLive) return false;

            // השידור התחיל עכשיו: קיק עלול להציג את הקטגוריה מהשידור הקודם.
            // זה לא שינוי ידני של הסטרימר – שולחים את הקטגוריה הנכונה לפי מה שפתוח עכשיו.
            if (wasLive == false && CanWrite())
            {
                var wanted = _engine.CurrentCategory;
                if (channel.Category is not null && IsSameAsOurs(channel.Category)) return false;
                _log.Log(LogLevel.Info, $"השידור התחיל – מעדכן את הקטגוריה בקיק ל-{wanted}",
                    $"Kick showed: {channel.Category?.Name ?? "none"}");
                var r = await _kick.SetCategoryAsync(wanted, null, ct).ConfigureAwait(false);
                if (r.Success)
                    _log.Log(LogLevel.Change, $"הקטגוריה בקיק עודכנה בתחילת השידור: {r.AppliedCategory ?? wanted}");
                else
                    _log.Log(LogLevel.Warning, $"לא ניתן לעדכן קטגוריה בתחילת השידור", r.Message);
                return false;
            }

            var cat = channel.Category;
            if (cat is null || IsSameAsOurs(cat)) return false;
            return _engine.SyncFromPlatform(cat.Name);
        }
        catch (Exception ex) when (ex is KickException or HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) return false;
            // לא מציפים את היומן באותה שגיאה כל דקה
            if (_lastError != ex.Message) _log.Log(LogLevel.Warning, "לא ניתן לקרוא את הערוץ מקיק", ex.Message);
            _lastError = ex.Message;
            return false;
        }
    }

    /// <summary>הקטגוריה בקיק היא זו שהתוכנה קבעה (אולי בשם מעט שונה / קטגוריית גיבוי)?</summary>
    private bool IsSameAsOurs(KickCategory cat) =>
        (_kick.LastSetCategory is { } last && last.Id == cat.Id) ||
        KickApiClient.SameName(cat.Name, _engine.CurrentCategory);

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();
}
