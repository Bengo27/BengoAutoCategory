namespace AutoCategory.Core.Platforms.Kick;

/// <summary>כתובות ה-API הרשמיות של Kick (מתוך docs.kick.com / KickEngineering/KickDevDocs).</summary>
public static class KickEndpoints
{
    public const string OAuthHost = "https://id.kick.com";
    public const string ApiHost = "https://api.kick.com";

    public const string Authorize = OAuthHost + "/oauth/authorize";
    public const string Token = OAuthHost + "/oauth/token";
    public const string Revoke = OAuthHost + "/oauth/revoke";

    public const string Users = ApiHost + "/public/v1/users";
    public const string Channels = ApiHost + "/public/v1/channels";
    public const string CategoriesV2 = ApiHost + "/public/v2/categories";
    public const string CategoriesV1 = ApiHost + "/public/v1/categories";

    /// <summary>ההרשאות שהתוכנה מבקשת: לקרוא משתמש, לקרוא ולעדכן את הערוץ.</summary>
    public const string Scopes = "user:read channel:read channel:write";

    /// <summary>הפורט המקומי שמקבל את החזרה מדף ההתחברות של קיק.</summary>
    public const int CallbackPort = 47653;
    public const string RedirectUri = "http://localhost:47653/callback";
}

public sealed record KickCategory(long Id, string Name, string? Thumbnail);

public sealed record KickChannelInfo(long BroadcasterUserId, string Slug, string? StreamTitle, KickCategory? Category, bool IsLive);

public sealed record KickTokenResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds, string? Scope);

/// <summary>מצב החיבור לקיק כפי שנשמר בדיסק (סודות – מוצפנים).</summary>
public sealed class KickAuthData
{
    public string? ClientId { get; set; }
    public string? ClientSecretProtected { get; set; }
    public string? AccessTokenProtected { get; set; }
    public string? RefreshTokenProtected { get; set; }
    public DateTime? AccessTokenExpiresUtc { get; set; }
    public string? Scope { get; set; }
    public string? Username { get; set; }
    public string? ChannelSlug { get; set; }
    public long? BroadcasterUserId { get; set; }
    public DateTime? ConnectedAtUtc { get; set; }
}

public sealed class KickException : Exception
{
    public int? StatusCode { get; }
    public KickException(string message, int? statusCode = null, Exception? inner = null) : base(message, inner) =>
        StatusCode = statusCode;
}
