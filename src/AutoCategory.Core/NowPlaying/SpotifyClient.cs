using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoCategory.Core.Models;
using AutoCategory.Core.Platforms.Kick;
using AutoCategory.Core.Services;
using AutoCategory.Core.Storage;

namespace AutoCategory.Core.NowPlaying;

public sealed class SpotifyAuthData
{
    public string? ClientId { get; set; }
    public string? AccessTokenProtected { get; set; }
    public string? RefreshTokenProtected { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}

/// <summary>
/// חיבור ל-Spotify Web API (התחברות PKCE עם Client ID של הסטרימר – בלי Secret),
/// וקריאת השיר שמתנגן. לפי הפרויקט המקורי של Bengo (server.mjs).
/// </summary>
public sealed class SpotifyClient : INowPlayingSource
{
    public const int CallbackPort = 4387;
    public const string RedirectUri = "http://127.0.0.1:4387/callback";
    public const string AuthorizeUrl = "https://accounts.spotify.com/authorize";
    public const string TokenUrl = "https://accounts.spotify.com/api/token";
    public const string CurrentlyPlayingUrl = "https://api.spotify.com/v1/me/player/currently-playing?additional_types=episode";

    private readonly HttpClient _http;
    private readonly ISecretProtector _protector;
    private readonly Action<string> _openBrowser;
    private readonly ILoggingService _log;
    private readonly Func<DateTime> _utcNow;
    private readonly string _file;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private SpotifyAuthData _data;

    public int Port { get; init; } = CallbackPort;
    public string Redirect { get; init; } = RedirectUri;
    public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public event Action? ConnectionChanged;

    public SpotifyClient(HttpClient http, string dataDirectory, ISecretProtector protector, Action<string> openBrowser,
        ILoggingService log, Func<DateTime>? utcNow = null)
    {
        _http = http;
        _protector = protector;
        _openBrowser = openBrowser;
        _log = log;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _file = Path.Combine(dataDirectory, "spotify_auth.json");
        try { _data = JsonStore.Load<SpotifyAuthData>(_file) ?? new SpotifyAuthData(); }
        catch { JsonStore.BackupCorrupt(_file); _data = new SpotifyAuthData(); }
    }

    public string? ClientId { get { lock (_lock) return _data.ClientId; } }
    public bool IsConnected { get { lock (_lock) return _data.RefreshTokenProtected is not null; } }

    public static bool IsValidClientId(string? id) => id is not null && Regex.IsMatch(id.Trim(), "^[0-9a-fA-F]{32}$");

    // ───────────────────────── התחברות ─────────────────────────

    public async Task ConnectAsync(string clientId, CancellationToken ct = default)
    {
        clientId = (clientId ?? "").Trim();
        if (!IsValidClientId(clientId))
            throw new SpotifyException("Client ID צריך להכיל 32 תווים (אותיות a-f ומספרים). לא Client Secret.");

        lock (_lock)
        {
            if (_data.ClientId != clientId) _data = new SpotifyAuthData { ClientId = clientId };
            Save();
        }

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var url = AuthorizeUrl + "?" + string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = Redirect,
            ["scope"] = "user-read-currently-playing",
            ["state"] = state,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge,
        }.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));

        IReadOnlyDictionary<string, string> query;
        try
        {
            using var listener = new LoopbackCallbackListener(Port) { ServiceName = "ספוטיפיי", AppName = "Catego Studio" };
            listener.Start();
            _openBrowser(url);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(LoginTimeout);
            try { query = await listener.WaitForCallbackAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new SpotifyException("עבר זמן רב מדי בלי אישור בדפדפן. נסה שוב.");
            }
        }
        catch (KickException ex)
        {
            // LoopbackCallbackListener זורק KickException כשהפורט תפוס
            throw new SpotifyException($"הפורט {Port} תפוס – אולי הכלי הישן (START.bat) עדיין פתוח? סגור אותו ונסה שוב.", ex);
        }

        if (query.TryGetValue("error", out var error))
            throw new SpotifyException(error == "access_denied" ? "ההתחברות בוטלה." : "ספוטיפיי לא אישר את ההתחברות: " + error);
        if (!query.TryGetValue("state", out var st) || st != state)
            throw new SpotifyException("תשובת ההתחברות לא תואמת. נסה שוב.");
        if (!query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            throw new SpotifyException("ספוטיפיי לא החזיר קוד התחברות.");

        await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = Redirect,
            ["code_verifier"] = verifier,
        }, ct).ConfigureAwait(false);
        _log.Log(LogLevel.Info, "התחברת לספוטיפיי");
        ConnectionChanged?.Invoke();
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            _data.AccessTokenProtected = null;
            _data.RefreshTokenProtected = null;
            _data.ExpiresUtc = null;
            Save();
        }
        _log.Log(LogLevel.Info, "התנתקת מספוטיפיי");
        ConnectionChanged?.Invoke();
    }

    private async Task TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = ClientId ?? throw new SpotifyException("חסר Client ID.");
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) };
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            if (form["grant_type"] == "refresh_token" && res.StatusCode == HttpStatusCode.BadRequest)
            {
                Disconnect();
                throw new SpotifyException("החיבור לספוטיפיי פג – התחבר מחדש.");
            }
            throw new SpotifyException("אימות ספוטיפיי נכשל. נסה להתחבר מחדש.");
        }
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        var access = r.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrEmpty(access)) throw new SpotifyException("תשובה לא צפויה מספוטיפיי.");
        var expires = r.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var sec) ? sec : 3600;
        lock (_lock)
        {
            _data.AccessTokenProtected = _protector.Protect(access);
            if (r.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { Length: > 0 } refresh)
                _data.RefreshTokenProtected = _protector.Protect(refresh);
            _data.ExpiresUtc = _utcNow().AddSeconds(expires);
            Save();
        }
    }

    private async Task EnsureFreshAsync(CancellationToken ct)
    {
        DateTime? exp;
        lock (_lock) exp = _data.ExpiresUtc;
        if (exp is not null && _utcNow() < exp.Value.AddSeconds(-60)) return;
        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_lock) exp = _data.ExpiresUtc;
            if (exp is not null && _utcNow() < exp.Value.AddSeconds(-60)) return;
            string? refresh;
            lock (_lock) refresh = _data.RefreshTokenProtected is { } p ? _protector.Unprotect(p) : null;
            if (refresh is null) throw new SpotifyException("לא מחובר לספוטיפיי.");
            await TokenAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh }, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    // ───────────────────────── מה מתנגן ─────────────────────────

    public async Task<NowPlayingRead> ReadAsync(CancellationToken ct)
    {
        if (!IsConnected) return new NowPlayingRead(null, "לא מחובר לספוטיפיי – התחבר בעמוד Now Playing.", TimeSpan.FromSeconds(5));
        try
        {
            await EnsureFreshAsync(ct).ConfigureAwait(false);
            string token;
            lock (_lock) token = _protector.Unprotect(_data.AccessTokenProtected!);
            using var req = new HttpRequestMessage(HttpMethod.Get, CurrentlyPlayingUrl);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var four = TimeSpan.FromSeconds(4);
            switch ((int)res.StatusCode)
            {
                case 204:
                    return new NowPlayingRead(null, null, four);
                case 401:
                    lock (_lock) _data.ExpiresUtc = null; // ירענן בפעם הבאה
                    return new NowPlayingRead(null, "מחדש חיבור לספוטיפיי…", TimeSpan.FromSeconds(1));
                case 403:
                    return new NowPlayingRead(null, "אין הרשאה: בדוק Premium והרשאת המשתמש באפליקציה, והתחבר מחדש.", TimeSpan.FromSeconds(30));
                case 429:
                    var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                    return new NowPlayingRead(null, "ספוטיפיי ביקש להמתין – החיבור יתחדש לבד.", wait < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : wait);
            }
            if (!res.IsSuccessStatusCode) return new NowPlayingRead(null, "ספוטיפיי לא זמין כרגע. מנסה שוב…", TimeSpan.FromSeconds(15));
            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new NowPlayingRead(Normalize(json, _utcNow()), null, four);
        }
        catch (SpotifyException ex)
        {
            return new NowPlayingRead(null, ex.Message, TimeSpan.FromSeconds(15));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            return new NowPlayingRead(null, "החיבור לספוטיפיי נכשל. בדוק אינטרנט או התחבר מחדש.", TimeSpan.FromSeconds(15));
        }
    }

    /// <summary>הופך את תשובת currently-playing לשיר (null = פרסומת / אין ניגון).</summary>
    public static NowPlayingTrack? Normalize(string json, DateTime utcNow)
    {
        using var doc = JsonDocument.Parse(json);
        var p = doc.RootElement;
        if (Str(p, "currently_playing_type") == "ad") return null;
        if (!p.TryGetProperty("item", out var t) || t.ValueKind != JsonValueKind.Object) return null;

        var artist = t.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
            ? string.Join(", ", artists.EnumerateArray().Select(a => Str(a, "name")).Where(n => !string.IsNullOrEmpty(n)))
            : t.TryGetProperty("show", out var show) ? Str(show, "publisher") ?? "" : "";
        string? image = null;
        if (t.TryGetProperty("album", out var album) && album.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array && imgs.GetArrayLength() > 0)
            image = Str(imgs[0], "url");
        else if (t.TryGetProperty("images", out var eimgs) && eimgs.ValueKind == JsonValueKind.Array && eimgs.GetArrayLength() > 0)
            image = Str(eimgs[0], "url");
        var url = t.TryGetProperty("external_urls", out var ext) ? Str(ext, "spotify") : null;

        return new NowPlayingTrack(
            Str(t, "id") ?? Str(t, "uri") ?? Str(t, "name") ?? "?",
            Str(t, "name") ?? "",
            artist,
            image,
            url,
            t.TryGetProperty("duration_ms", out var d) && d.TryGetInt64(out var dur) ? dur : 0,
            p.TryGetProperty("progress_ms", out var pr) && pr.TryGetInt64(out var prog) ? prog : 0,
            p.TryGetProperty("is_playing", out var ip) && ip.ValueKind == JsonValueKind.True,
            utcNow);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private void Save()
    {
        try { JsonStore.Save(_file, _data); } catch { }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class SpotifyException : Exception
{
    public SpotifyException(string message, Exception? inner = null) : base(message, inner) { }
}
