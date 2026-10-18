using AutoCategory.Core.Storage;

namespace AutoCategory.Core.Platforms.Kick;

/// <summary>הצפנה/פענוח של סודות. ב-Windows: DPAPI (מוצפן לחשבון המשתמש הנוכחי בלבד).</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string protectedValue);
}

/// <summary>ללא הצפנה – לבדיקות בלבד.</summary>
public sealed class PlainTextProtector : ISecretProtector
{
    public string Protect(string plain) => "plain:" + plain;
    public string Unprotect(string protectedValue) =>
        protectedValue.StartsWith("plain:", StringComparison.Ordinal) ? protectedValue[6..] : protectedValue;
}

/// <summary>שמירת פרטי החיבור לקיק בקובץ kick_auth.json (הסודות מוצפנים).</summary>
public sealed class KickAuthStore
{
    private readonly object _lock = new();
    private readonly ISecretProtector _protector;
    private KickAuthData _data;

    public string FilePath { get; }

    public KickAuthStore(string dataDirectory, ISecretProtector protector)
    {
        _protector = protector;
        FilePath = Path.Combine(dataDirectory, "kick_auth.json");
        try { _data = JsonStore.Load<KickAuthData>(FilePath) ?? new KickAuthData(); }
        catch { JsonStore.BackupCorrupt(FilePath); _data = new KickAuthData(); }
    }

    public KickAuthData Snapshot()
    {
        lock (_lock)
        {
            return new KickAuthData
            {
                ClientId = _data.ClientId,
                ClientSecretProtected = _data.ClientSecretProtected,
                AccessTokenProtected = _data.AccessTokenProtected,
                RefreshTokenProtected = _data.RefreshTokenProtected,
                AccessTokenExpiresUtc = _data.AccessTokenExpiresUtc,
                Scope = _data.Scope,
                Username = _data.Username,
                ChannelSlug = _data.ChannelSlug,
                BroadcasterUserId = _data.BroadcasterUserId,
                ConnectedAtUtc = _data.ConnectedAtUtc,
            };
        }
    }

    public string? ClientId { get { lock (_lock) return _data.ClientId; } }
    public string? ClientSecret => Get(d => d.ClientSecretProtected);
    public string? AccessToken => Get(d => d.AccessTokenProtected);
    public string? RefreshToken => Get(d => d.RefreshTokenProtected);
    public bool HasCredentials => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
    public bool HasTokens => !string.IsNullOrEmpty(RefreshToken) || !string.IsNullOrEmpty(AccessToken);

    public void SetCredentials(string clientId, string clientSecret)
    {
        lock (_lock)
        {
            var changed = _data.ClientId != clientId.Trim();
            _data.ClientId = clientId.Trim();
            _data.ClientSecretProtected = _protector.Protect(clientSecret.Trim());
            if (changed) ClearTokensNoLock(); // אפליקציה אחרת = טוקנים לא תקפים
            Save();
        }
    }

    public void ClearCredentials()
    {
        lock (_lock)
        {
            _data.ClientId = null;
            _data.ClientSecretProtected = null;
            ClearTokensNoLock();
            Save();
        }
    }

    public void SetTokens(KickTokenResponse t, DateTime nowUtc)
    {
        lock (_lock)
        {
            _data.AccessTokenProtected = _protector.Protect(t.AccessToken);
            if (!string.IsNullOrEmpty(t.RefreshToken))
                _data.RefreshTokenProtected = _protector.Protect(t.RefreshToken);
            _data.AccessTokenExpiresUtc = nowUtc.AddSeconds(Math.Max(60, t.ExpiresInSeconds));
            if (!string.IsNullOrEmpty(t.Scope)) _data.Scope = t.Scope;
            Save();
        }
    }

    public void SetAccount(string? username, string? slug, long? broadcasterId, DateTime nowUtc)
    {
        lock (_lock)
        {
            _data.Username = username;
            _data.ChannelSlug = slug;
            _data.BroadcasterUserId = broadcasterId;
            _data.ConnectedAtUtc ??= nowUtc;
            Save();
        }
    }

    public void ClearTokens()
    {
        lock (_lock)
        {
            ClearTokensNoLock();
            Save();
        }
    }

    private void ClearTokensNoLock()
    {
        _data.AccessTokenProtected = null;
        _data.RefreshTokenProtected = null;
        _data.AccessTokenExpiresUtc = null;
        _data.Scope = null;
        _data.Username = null;
        _data.ChannelSlug = null;
        _data.BroadcasterUserId = null;
        _data.ConnectedAtUtc = null;
    }

    private string? Get(Func<KickAuthData, string?> pick)
    {
        string? value;
        lock (_lock) value = pick(_data);
        if (string.IsNullOrEmpty(value)) return null;
        try { return _protector.Unprotect(value); }
        catch { return null; } // למשל קובץ שהועתק ממשתמש אחר – אי אפשר לפענח
    }

    private void Save()
    {
        try { JsonStore.Save(FilePath, _data); } catch { /* ננסה שוב בפעם הבאה */ }
    }
}
