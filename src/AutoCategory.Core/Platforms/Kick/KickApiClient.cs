using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoCategory.Core.Platforms.Kick;

/// <summary>
/// לקוח HTTP ל-API הרשמי של Kick: OAuth 2.1 עם PKCE, רענון טוקן אוטומטי, ערוץ וקטגוריות.
/// כל הכתובות לפי התיעוד הרשמי (KickEngineering/KickDevDocs + api.kick.com/swagger).
/// </summary>
public sealed class KickApiClient
{
    private readonly HttpClient _http;
    private readonly KickAuthStore _store;
    private readonly Func<DateTime> _utcNow;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public KickApiClient(HttpClient http, KickAuthStore store, Func<DateTime>? utcNow = null)
    {
        _http = http;
        _store = store;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    // ───────────────────────── שרת ההתחברות (broker) ─────────────────────────

    /// <summary>כתובת השרת שמחזיק את ה-Client Secret (Cloudflare Worker). null = אין.</summary>
    public string? BrokerUrl { get; set; }

    /// <summary>ה-Client ID של האפליקציה המובנית (ציבורי, לא סוד).</summary>
    public string? BrokerClientId { get; set; }

    /// <summary>האם המשתמש הגדיר אפליקציית קיק משלו (Client ID + Secret).</summary>
    public bool UsesOwnApp => _store.HasCredentials;

    /// <summary>התחברות דרך השרת: אין אפליקציה משלך, ויש שרת מוגדר.</summary>
    public bool UsesBroker => !UsesOwnApp && BrokerAvailable;

    public bool BrokerAvailable => !string.IsNullOrWhiteSpace(BrokerUrl) && !string.IsNullOrWhiteSpace(BrokerClientId);

    /// <summary>ה-Client ID שבו משתמשים בפועל.</summary>
    public string? EffectiveClientId => UsesOwnApp ? _store.ClientId : UsesBroker ? BrokerClientId : null;

    public bool CanConnect => EffectiveClientId is not null;

    // ───────────────────────── PKCE ─────────────────────────

    public sealed record PkcePair(string Verifier, string Challenge);

    public static PkcePair CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32)); // 43 תווים
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new PkcePair(verifier, challenge);
    }

    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(24));

    public static string BuildAuthorizeUrl(string clientId, string redirectUri, string scopes, string state, string codeChallenge) =>
        KickEndpoints.Authorize +
        "?response_type=code" +
        "&client_id=" + Uri.EscapeDataString(clientId) +
        "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
        "&scope=" + Uri.EscapeDataString(scopes) +
        "&code_challenge=" + Uri.EscapeDataString(codeChallenge) +
        "&code_challenge_method=S256" +
        "&state=" + Uri.EscapeDataString(state);

    // ───────────────────────── טוקנים ─────────────────────────

    public async Task<KickTokenResponse> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        KickTokenResponse tokens;
        if (UsesBroker)
        {
            tokens = await PostBrokerAsync("token", new Dictionary<string, string>
            {
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = redirectUri,
            }, ct).ConfigureAwait(false);
        }
        else
        {
            var (clientId, secret) = RequireCredentials();
            tokens = await PostTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["client_secret"] = secret,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier,
                ["code"] = code,
            }, ct).ConfigureAwait(false);
        }
        _store.SetTokens(tokens, _utcNow());
        return tokens;
    }

    /// <summary>מרענן את הטוקן. כל רענון מחליף גם את ה-refresh token, ולכן רק רענון אחד בכל פעם.</summary>
    public async Task RefreshAsync(CancellationToken ct, string? staleAccessToken = null)
    {
        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // ייתכן שבקשה אחרת כבר רעננה בזמן שחיכינו
            if (staleAccessToken is not null && _store.AccessToken != staleAccessToken && !IsExpired()) return;

            var refresh = _store.RefreshToken ?? throw new KickException("החיבור לקיק פג – יש להתחבר מחדש.", 401);
            KickTokenResponse tokens;
            try
            {
                if (UsesBroker)
                {
                    tokens = await PostBrokerAsync("refresh", new Dictionary<string, string>
                    {
                        ["refresh_token"] = refresh,
                    }, ct).ConfigureAwait(false);
                }
                else
                {
                    var (clientId, secret) = RequireCredentials();
                    tokens = await PostTokenAsync(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["client_id"] = clientId,
                        ["client_secret"] = secret,
                        ["refresh_token"] = refresh,
                    }, ct).ConfigureAwait(false);
                }
            }
            catch (KickException ex) when (ex.StatusCode is 400 or 401)
            {
                _store.ClearTokens();
                throw new KickException("החיבור לקיק פג או בוטל – יש להתחבר מחדש.", 401, ex);
            }
            _store.SetTokens(tokens, _utcNow());
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task RevokeAsync(CancellationToken ct)
    {
        var token = _store.RefreshToken ?? _store.AccessToken;
        if (token is null) return;
        var hint = _store.RefreshToken is not null ? "refresh_token" : "access_token";
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{KickEndpoints.Revoke}?token={Uri.EscapeDataString(token)}&token_hint_type={hint}")
        {
            Content = new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()),
        };
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        // ניתוק מקומי קורה בכל מקרה
    }

    private bool IsExpired()
    {
        var exp = _store.Snapshot().AccessTokenExpiresUtc;
        return exp is null || _utcNow() >= exp.Value.AddSeconds(-60);
    }

    private async Task<KickTokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, KickEndpoints.Token) { Content = new FormUrlEncodedContent(form) };
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw new KickException($"קיק דחה את בקשת ההתחברות ({(int)res.StatusCode}): {Short(body)}", (int)res.StatusCode);
        return ParseTokens(body);
    }

    /// <summary>שליחה לשרת ההתחברות (שמוסיף את ה-Secret ומעביר לקיק).</summary>
    private async Task<KickTokenResponse> PostBrokerAsync(string action, Dictionary<string, string> payload, CancellationToken ct)
    {
        var url = BrokerUrl!.TrimEnd('/') + "/" + action;
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new KickException("שרת ההתחברות לא זמין כרגע. בדוק את החיבור לאינטרנט ונסה שוב.", null, ex);
        }
        using (res)
        {
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                throw new KickException($"ההתחברות נדחתה ({(int)res.StatusCode}): {Short(body)}", (int)res.StatusCode);
            return ParseTokens(body);
        }
    }

    private static KickTokenResponse ParseTokens(string body)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException ex) { throw new KickException("תשובה לא תקינה משרת ההתחברות.", null, ex); }
        using var _ = doc;
        var root = doc.RootElement;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object) root = data;

        var access = Str(root, "access_token");
        if (string.IsNullOrEmpty(access)) throw new KickException("תשובה לא צפויה משרת ההתחברות של קיק (אין access_token).");
        return new KickTokenResponse(access, Str(root, "refresh_token") ?? "", Int(root, "expires_in") ?? 3600, Str(root, "scope"));
    }

    private (string ClientId, string Secret) RequireCredentials()
    {
        var id = _store.ClientId;
        var secret = _store.ClientSecret;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            throw new KickException("חסרים Client ID / Client Secret של אפליקציית הקיק.");
        return (id, secret);
    }

    // ───────────────────────── API ─────────────────────────

    /// <summary>המשתמש המחובר: (user_id, name).</summary>
    public async Task<(long Id, string Name)> GetCurrentUserAsync(CancellationToken ct)
    {
        using var doc = await SendAuthorizedAsync(HttpMethod.Get, KickEndpoints.Users, null, ct).ConfigureAwait(false);
        var first = FirstData(doc!.RootElement) ?? throw new KickException("קיק לא החזיר פרטי משתמש.");
        return (Long(first, "user_id") ?? 0, Str(first, "name") ?? "");
    }

    /// <summary>הערוץ של המשתמש המחובר (כולל הקטגוריה הנוכחית).</summary>
    public async Task<KickChannelInfo> GetMyChannelAsync(CancellationToken ct)
    {
        using var doc = await SendAuthorizedAsync(HttpMethod.Get, KickEndpoints.Channels, null, ct).ConfigureAwait(false);
        var c = FirstData(doc!.RootElement) ?? throw new KickException("קיק לא החזיר את פרטי הערוץ.");
        KickCategory? category = null;
        if (c.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.Object)
        {
            var id = Long(cat, "id");
            var name = Str(cat, "name");
            if (id is > 0 && !string.IsNullOrEmpty(name)) category = new KickCategory(id.Value, name, Str(cat, "thumbnail"));
        }
        var isLive = c.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.Object &&
                     s.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True;
        return new KickChannelInfo(Long(c, "broadcaster_user_id") ?? 0, Str(c, "slug") ?? "", Str(c, "stream_title"), category, isLive);
    }

    /// <summary>PATCH /public/v1/channels עם category_id (מחזיר 204 בהצלחה).</summary>
    public async Task UpdateCategoryAsync(long categoryId, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["category_id"] = categoryId });
        using var _ = await SendAuthorizedAsync(HttpMethod.Patch, KickEndpoints.Channels, json, ct).ConfigureAwait(false);
    }

    /// <summary>חיפוש קטגוריות לפי טקסט חופשי (לחלון הכללים).</summary>
    public async Task<IReadOnlyList<KickCategory>> SearchCategoriesAsync(string query, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length < 2) return Array.Empty<KickCategory>();

        // v1 תומך בחיפוש חופשי (q) אבל מסומן כ"יוצא משימוש"; אם הוא לא זמין – עוברים ל-v2 (סינון לפי שם)
        try
        {
            var v1 = await GetCategoriesAsync($"{KickEndpoints.CategoriesV1}?q={Uri.EscapeDataString(query)}", ct).ConfigureAwait(false);
            if (v1.Count > 0) return v1;
        }
        catch (KickException ex) when (ex.StatusCode is 400 or 404 or 410)
        {
        }

        if (query.Length < 3) return Array.Empty<KickCategory>();
        return await GetCategoriesAsync($"{KickEndpoints.CategoriesV2}?name={Uri.EscapeDataString(query)}&limit=50", ct).ConfigureAwait(false);
    }

    /// <summary>מחפש קטגוריה בשם המדויק (לא תלוי באותיות גדולות/קטנות).</summary>
    public async Task<KickCategory?> FindCategoryByExactNameAsync(string name, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length == 0) return null;

        if (name.Length >= 3)
        {
            try
            {
                var v2 = await GetCategoriesAsync($"{KickEndpoints.CategoriesV2}?name={Uri.EscapeDataString(name)}&limit=100", ct).ConfigureAwait(false);
                var hit = v2.FirstOrDefault(c => SameName(c.Name, name));
                if (hit is not null) return hit;
            }
            catch (KickException ex) when (ex.StatusCode is 400 or 404)
            {
            }
        }

        var search = await SearchCategoriesAsync(name, ct).ConfigureAwait(false);
        return search.FirstOrDefault(c => SameName(c.Name, name));
    }

    /// <summary>
    /// התאמה חכמה: קודם שם מדויק, ואז גרסאות של השם (GTA V, Grand Theft Auto 5, Overwatch בלי ה-2 וכו').
    /// Exact=false אומר שנמצאה התאמה לגרסה של השם ולא לשם עצמו.
    /// </summary>
    public async Task<(KickCategory? Match, bool Exact)> FindBestCategoryAsync(string name, CancellationToken ct)
    {
        var exact = await FindCategoryByExactNameAsync(name, ct).ConfigureAwait(false);
        if (exact is not null) return (exact, true);

        var original = NormalizeName(name);
        KickCategory? prefixCandidate = null;
        foreach (var variant in NameVariants(name).Skip(1).Take(6))
        {
            IReadOnlyList<KickCategory> results;
            try { results = await SearchCategoriesAsync(variant, ct).ConfigureAwait(false); }
            catch (KickException ex) when (ex.StatusCode is 400 or 404) { continue; }

            var hit = results.FirstOrDefault(c => SameName(c.Name, variant));
            if (hit is not null) return (hit, false);

            // למשל "Grand Theft Auto V (GTA)" כשחיפשנו "Grand Theft Auto V"
            prefixCandidate ??= results.FirstOrDefault(c =>
                original.Length >= 6 && NormalizeName(c.Name).StartsWith(original, StringComparison.Ordinal));
        }

        if (prefixCandidate is null && original.Length >= 6)
        {
            var direct = await SearchCategoriesAsync(name, ct).ConfigureAwait(false);
            prefixCandidate = direct.FirstOrDefault(c => NormalizeName(c.Name).StartsWith(original, StringComparison.Ordinal));
        }
        return (prefixCandidate, false);
    }

    /// <summary>שמות מוכרים שונים לאותו משחק (אפשר להרחיב).</summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Grand Theft Auto V"] = new[] { "GTA V", "GTA 5", "Grand Theft Auto V (GTA)", "Grand Theft Auto" },
        ["Overwatch 2"] = new[] { "Overwatch" },
        ["Counter-Strike 2"] = new[] { "Counter-Strike", "CS2" },
        ["League of Legends"] = new[] { "LoL" },
        ["Games + Demos"] = new[] { "Games & Demos", "Games and Demos" },
        ["Apex Legends"] = new[] { "Apex" },
        ["Rocket League"] = new[] { "Rocket League®" },
    };

    private static readonly string[] Romans = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

    /// <summary>גרסאות של שם קטגוריה לחיפוש. הראשונה היא תמיד השם המקורי.</summary>
    public static IReadOnlyList<string> NameVariants(string name)
    {
        var list = new List<string>();
        void Add(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && !list.Any(x => SameName(x, s))) list.Add(s);
        }

        Add(name);
        if (Aliases.TryGetValue(name.Trim(), out var aliases)) foreach (var a in aliases) Add(a);

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length >= 2)
        {
            var last = words[^1];
            var romanIndex = Array.FindIndex(Romans, r => r.Equals(last, StringComparison.OrdinalIgnoreCase));
            var isNumber = int.TryParse(last, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 10;
            if (romanIndex >= 0 || isNumber)
            {
                var stem = string.Join(' ', words[..^1]);
                var alt = isNumber ? Romans[n - 1] : (romanIndex + 1).ToString(CultureInfo.InvariantCulture);
                Add($"{stem} {alt}");
                var acronym = Acronym(words[..^1]);
                if (acronym.Length >= 2) { Add($"{acronym} {last}"); Add($"{acronym} {alt}"); }
                Add(stem);
            }
            else if (words.Length >= 3)
            {
                Add(Acronym(words));
            }
        }
        return list;
    }

    private static string Acronym(IEnumerable<string> words) =>
        string.Concat(words.Where(w => w.Length > 0 && char.IsLetter(w[0])).Select(w => char.ToUpperInvariant(w[0])));

    public async Task<KickCategory?> GetCategoryByIdAsync(long id, CancellationToken ct)
    {
        var list = await GetCategoriesAsync($"{KickEndpoints.CategoriesV2}?id={id}", ct).ConfigureAwait(false);
        return list.FirstOrDefault(c => c.Id == id);
    }

    private async Task<IReadOnlyList<KickCategory>> GetCategoriesAsync(string url, CancellationToken ct)
    {
        using var doc = await SendAuthorizedAsync(HttpMethod.Get, url, null, ct).ConfigureAwait(false);
        var list = new List<KickCategory>();
        if (doc!.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in data.EnumerateArray())
            {
                var id = Long(c, "id");
                var name = Str(c, "name");
                if (id is > 0 && !string.IsNullOrEmpty(name)) list.Add(new KickCategory(id.Value, name, Str(c, "thumbnail")));
            }
        }
        return list;
    }

    /// <summary>שולח בקשה עם Bearer token; מרענן מראש אם פג, ומנסה שוב פעם אחת אחרי 401.</summary>
    private async Task<JsonDocument?> SendAuthorizedAsync(HttpMethod method, string url, string? jsonBody, CancellationToken ct)
    {
        if (!_store.HasTokens) throw new KickException("לא מחובר לקיק.", 401);
        if (IsExpired() && _store.RefreshToken is not null) await RefreshAsync(ct).ConfigureAwait(false);

        for (var attempt = 0; ; attempt++)
        {
            var token = _store.AccessToken ?? throw new KickException("לא מחובר לקיק.", 401);
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (jsonBody is not null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && _store.RefreshToken is not null)
            {
                await RefreshAsync(ct, staleAccessToken: token).ConfigureAwait(false);
                continue;
            }

            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                throw new KickException(DescribeError((int)res.StatusCode, body), (int)res.StatusCode);

            if (res.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(body)) return null;
            try { return JsonDocument.Parse(body); }
            catch (JsonException ex) { throw new KickException("תשובה לא תקינה מקיק.", (int)res.StatusCode, ex); }
        }
    }

    private static string DescribeError(int status, string body) => status switch
    {
        401 => "החיבור לקיק פג – יש להתחבר מחדש.",
        403 => "לקיק אין הרשאה לפעולה הזו. ייתכן שחסרה הרשאת channel:write – התחבר מחדש.",
        429 => "יותר מדי בקשות לקיק – ננסה שוב מאוחר יותר.",
        >= 500 => $"שגיאה בשרת של קיק ({status}). נסה שוב בעוד רגע.",
        _ => $"קיק החזיר שגיאה {status}: {Short(body)}",
    };

    // ───────────────────────── עזר ─────────────────────────

    private static JsonElement? FirstData(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)) return null;
        if (data.ValueKind == JsonValueKind.Array) return data.GetArrayLength() > 0 ? data[0] : null;
        if (data.ValueKind == JsonValueKind.Object) return data;
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    private static long? Long(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
        return null;
    }

    private static int? Int(JsonElement e, string name)
    {
        var l = Long(e, name);
        return l is null ? null : (int)Math.Clamp(l.Value, 0, int.MaxValue);
    }

    private static string Short(string s)
    {
        s = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length > 200 ? s[..200] + "…" : s;
    }

    public static bool SameName(string a, string b) =>
        string.Equals(NormalizeName(a), NormalizeName(b), StringComparison.Ordinal);

    /// <summary>"Counter-Strike 2" ו-"counter strike 2" נחשבים אותו שם.</summary>
    public static string NormalizeName(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim())
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
