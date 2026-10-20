using System.Collections.Concurrent;
using System.Globalization;
using AutoCategory.Core.Models;
using AutoCategory.Core.Services;

namespace AutoCategory.Core.Platforms.Kick;

/// <summary>
/// חיבור לקיק: התחברות (OAuth + PKCE דרך הדפדפן), קריאת הקטגוריה הנוכחית ושינוי קטגוריה.
/// </summary>
public sealed class KickPlatform : IStreamingPlatform, IKickRewardsApi
{
    private readonly KickAuthStore _store;
    private readonly KickApiClient _api;
    private readonly ILoggingService _log;
    private readonly Action<string> _openBrowser;
    private readonly Func<DateTime> _utcNow;
    private readonly ConcurrentDictionary<string, KickCategory> _categoryCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>נקרא כשמזהה קטגוריה נמצא לפי שם – כדי לשמור אותו בכלל ולחסוך חיפוש בפעם הבאה.</summary>
    public event Action<string, long>? CategoryIdResolved;
    /// <summary>מצב החיבור השתנה (התחברות / ניתוק / פג תוקף).</summary>
    public event Action? ConnectionChanged;

    /// <summary>קטגוריית גיבוי למשחק שאין לו קטגוריה בקיק (null = כבוי).</summary>
    public Func<string?>? FallbackCategory { get; set; }

    public int CallbackPort { get; init; } = KickEndpoints.CallbackPort;
    public string RedirectUri { get; init; } = KickEndpoints.RedirectUri;
    public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>ההרשאות שמבקשים מקיק בהתחברות (שונה בין Catego ל-Studio).</summary>
    public string Scopes { get; init; } = KickEndpoints.Scopes;

    public KickPlatform(KickAuthStore store, KickApiClient api, ILoggingService log, Action<string> openBrowser,
        Func<DateTime>? utcNow = null)
    {
        _store = store;
        _api = api;
        _log = log;
        _openBrowser = openBrowser;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string Name => "קיק";
    public bool IsConnected => _store.HasTokens;
    /// <summary>אפשר להתחבר: יש אפליקציה משלך, או שרת התחברות מובנה.</summary>
    public bool HasCredentials => _api.CanConnect;
    /// <summary>מחובר דרך שרת ההתחברות המובנה (בלי אפליקציה משלך).</summary>
    public bool UsesBroker => _api.UsesBroker;
    public bool UsesOwnApp => _api.UsesOwnApp;
    public bool BrokerAvailable => _api.BrokerAvailable;
    public string? AccountName => _store.Snapshot().Username;
    public string? ChannelSlug => _store.Snapshot().ChannelSlug;
    public string? ClientId => _store.ClientId;
    public string? EffectiveClientId => _api.EffectiveClientId;
    public KickApiClient Api => _api;

    /// <summary>
    /// הקטגוריה האחרונה שהתוכנה קבעה (מזהה בקיק + השם שלנו). משמש בסנכרון כדי לא לפרש
    /// הבדל קטן בשם (למשל "Overwatch 2" אצלנו מול "Overwatch" בקיק) כשינוי ידני.
    /// </summary>
    public (long Id, string OurName)? LastSetCategory { get; private set; }

    public void SetCredentials(string clientId, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new ArgumentException("יש להזין Client ID ו-Client Secret.");
        var wasConnected = IsConnected;
        _store.SetCredentials(clientId, clientSecret);
        if (wasConnected && !IsConnected) ConnectionChanged?.Invoke();
    }

    /// <summary>חזרה לאפליקציה המובנית: מוחק את ה-Client ID/Secret שלך ומנתק.</summary>
    public async Task UseBuiltInAppAsync()
    {
        if (IsConnected) await DisconnectAsync().ConfigureAwait(false);
        _store.ClearCredentials();
        ConnectionChanged?.Invoke();
    }

    // ───────────────────────── התחברות ─────────────────────────

    /// <summary>
    /// פותח את דף ההתחברות של קיק בדפדפן ומחכה שהמשתמש יאשר.
    /// מחזיר את שם המשתמש שהתחבר.
    /// </summary>
    public async Task<string> ConnectAsync(CancellationToken ct = default)
    {
        if (!_api.CanConnect)
            throw new KickException("קודם צריך להזין Client ID ו-Client Secret של אפליקציית הקיק שלך.");

        var pkce = KickApiClient.CreatePkce();
        var state = KickApiClient.CreateState();
        var url = KickApiClient.BuildAuthorizeUrl(_api.EffectiveClientId!, RedirectUri, Scopes, state, pkce.Challenge);

        using var listener = new LoopbackCallbackListener(CallbackPort);
        listener.Start();

        _log.Log(LogLevel.Info, "נפתח דף ההתחברות של קיק בדפדפן – ממתין לאישור…");
        _openBrowser(url);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(LoginTimeout);

        IReadOnlyDictionary<string, string> query;
        try
        {
            query = await listener.WaitForCallbackAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new KickException("עבר זמן רב מדי בלי אישור בדפדפן. נסה שוב.");
        }

        if (query.TryGetValue("error", out var error))
        {
            var desc = query.TryGetValue("error_description", out var d) ? d : error;
            throw new KickException($"קיק לא אישר את ההתחברות: {desc}");
        }
        if (!query.TryGetValue("state", out var returnedState) || returnedState != state)
            throw new KickException("תשובת ההתחברות לא תואמת (state). נסה שוב.");
        if (!query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            throw new KickException("קיק לא החזיר קוד התחברות.");

        await _api.ExchangeCodeAsync(code, pkce.Verifier, RedirectUri, ct).ConfigureAwait(false);

        string username = "";
        long? userId = null;
        try
        {
            var user = await _api.GetCurrentUserAsync(ct).ConfigureAwait(false);
            username = user.Name;
            userId = user.Id;
        }
        catch (KickException ex)
        {
            _log.Log(LogLevel.Warning, "ההתחברות הצליחה אבל לא ניתן לקרוא את שם המשתמש", ex.Message);
        }

        string? slug = null;
        try
        {
            var channel = await _api.GetMyChannelAsync(ct).ConfigureAwait(false);
            slug = channel.Slug;
            userId ??= channel.BroadcasterUserId;
            if (string.IsNullOrEmpty(username)) username = channel.Slug;
        }
        catch (KickException ex)
        {
            _log.Log(LogLevel.Warning, "לא ניתן לקרוא את פרטי הערוץ", ex.Message);
        }

        _store.SetAccount(username, slug, userId, _utcNow());
        _log.Log(LogLevel.Info, $"התחברת לקיק בתור {username}");
        ConnectionChanged?.Invoke();
        return username;
    }

    public async Task DisconnectAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _api.RevokeAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // גם אם הביטול בשרת נכשל – מנתקים מקומית
        }
        _store.ClearTokens();
        _categoryCache.Clear();
        _log.Log(LogLevel.Info, "התנתקת מקיק");
        ConnectionChanged?.Invoke();
    }

    // ───────────────────────── ערוץ וקטגוריה ─────────────────────────

    public async Task<KickChannelInfo> GetChannelAsync(CancellationToken ct = default) =>
        await Guard(() => _api.GetMyChannelAsync(ct)).ConfigureAwait(false);

    public async Task<string?> GetCurrentCategoryAsync(CancellationToken cancellationToken = default) =>
        (await GetChannelAsync(cancellationToken).ConfigureAwait(false)).Category?.Name;

    public async Task<CategoryChangeResult> SetCategoryAsync(string categoryName, string? platformCategoryId,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new CategoryChangeResult(false, "לא מחובר לקיק.");
        try
        {
            KickCategory? category = null;
            if (long.TryParse(platformCategoryId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                category = new KickCategory(id, categoryName, null);
            else
                category = await ResolveCategoryAsync(categoryName, cancellationToken).ConfigureAwait(false);

            string? applied = null;
            if (category is null)
            {
                var fallback = FallbackCategory?.Invoke();
                if (!string.IsNullOrWhiteSpace(fallback) && !KickApiClient.SameName(fallback, categoryName))
                {
                    category = await ResolveCategoryAsync(fallback, cancellationToken).ConfigureAwait(false);
                    if (category is not null) applied = category.Name;
                }
            }

            if (category is null)
                return new CategoryChangeResult(false,
                    $"הקטגוריה \"{categoryName}\" לא נמצאה בקיק (וגם לא קטגוריית הגיבוי). ערוך את הכלל ובחר קטגוריה מהחיפוש של קיק.");

            await Guard(async () => { await _api.UpdateCategoryAsync(category.Id, cancellationToken).ConfigureAwait(false); return true; })
                .ConfigureAwait(false);
            LastSetCategory = (category.Id, categoryName);
            return new CategoryChangeResult(true, $"category_id={category.Id}", applied);
        }
        catch (KickException ex)
        {
            return new CategoryChangeResult(false, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new CategoryChangeResult(false, "אין חיבור לאינטרנט / לקיק: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new CategoryChangeResult(false, "קיק לא ענה בזמן.");
        }
    }

    /// <summary>מוצא קטגוריה בקיק לפי שם (עם מטמון).</summary>
    public async Task<KickCategory?> ResolveCategoryAsync(string name, CancellationToken ct = default)
    {
        if (_categoryCache.TryGetValue(name.Trim(), out var cached)) return cached;
        var (found, _) = await Guard(() => _api.FindBestCategoryAsync(name, ct)).ConfigureAwait(false);
        if (found is not null)
        {
            _categoryCache[name.Trim()] = found;
            CategoryIdResolved?.Invoke(name.Trim(), found.Id);
        }
        return found;
    }

    /// <summary>לבדיקת הכללים: התאמה + האם היא מדויקת.</summary>
    public Task<(KickCategory? Match, bool Exact)> FindBestCategoryAsync(string name, CancellationToken ct = default) =>
        Guard(() => _api.FindBestCategoryAsync(name, ct));

    public Task<IReadOnlyList<KickCategory>> SearchCategoriesAsync(string query, CancellationToken ct = default) =>
        Guard(() => _api.SearchCategoriesAsync(query, ct));

    // ───────────────────────── פרסי נקודות ─────────────────────────

    public bool HasRewardsScope
    {
        get
        {
            var scope = _store.Snapshot().Scope;
            // אם קיק לא החזיר רשימת הרשאות – מנסים, ואם תהיה שגיאה 403 נבקש להתחבר מחדש
            return string.IsNullOrWhiteSpace(scope) ||
                   scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(KickEndpoints.RewardsScope);
        }
    }

    public Task<IReadOnlyList<KickReward>> GetRewardsAsync(CancellationToken ct) => Guard(() => _api.GetRewardsAsync(ct));

    public Task<KickReward> CreateRewardAsync(string title, int cost, string description, string color, CancellationToken ct) =>
        Guard(() => _api.CreateRewardAsync(title, cost, description, color, ct));

    public Task<KickReward> UpdateRewardAsync(string id, IReadOnlyDictionary<string, object> changes, CancellationToken ct) =>
        Guard(() => _api.UpdateRewardAsync(id, changes, ct));

    public Task<IReadOnlyList<KickRedemption>> GetPendingRedemptionsAsync(string rewardId, CancellationToken ct) =>
        Guard(() => _api.GetPendingRedemptionsAsync(rewardId, ct));

    public Task AcceptRedemptionsAsync(IReadOnlyList<string> ids, CancellationToken ct) =>
        Guard(async () => { await _api.AcceptRedemptionsAsync(ids, ct).ConfigureAwait(false); return true; });

    public Task RejectRedemptionsAsync(IReadOnlyList<string> ids, CancellationToken ct) =>
        Guard(async () => { await _api.RejectRedemptionsAsync(ids, ct).ConfigureAwait(false); return true; });

    public Task<IReadOnlyDictionary<long, string>> GetUserNamesAsync(IReadOnlyList<long> userIds, CancellationToken ct) =>
        Guard(() => _api.GetUserNamesAsync(userIds, ct));

    /// <summary>אם קיק אומר שהחיבור פג – מעדכן את מצב החיבור.</summary>
    private async Task<T> Guard<T>(Func<Task<T>> action)
    {
        var wasConnected = IsConnected;
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            if (wasConnected && !IsConnected)
            {
                _log.Log(LogLevel.Warning, "החיבור לקיק פג – יש להתחבר מחדש בעמוד \"חיבור לקיק\"");
                ConnectionChanged?.Invoke();
            }
        }
    }
}
