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
    public const string Rewards = ApiHost + "/public/v1/channels/rewards";
    public const string Redemptions = Rewards + "/redemptions";

    /// <summary>ההרשאות של Catego: לקרוא משתמש, לקרוא ולעדכן את הערוץ (קטגוריה).</summary>
    public const string Scopes = "user:read channel:read channel:write";

    /// <summary>ההרשאות של Catego Studio: משתמש, ערוץ, ופרסי נקודות (הקראה).</summary>
    public const string StudioScopes = "user:read channel:read channel:rewards:read channel:rewards:write";

    public const string RewardsScope = "channel:rewards:write";

    /// <summary>הפורט המקומי שמקבל את החזרה מדף ההתחברות של קיק.</summary>
    public const int CallbackPort = 47653;
    public const string RedirectUri = "http://localhost:47653/callback";
}

public sealed record KickCategory(long Id, string Name, string? Thumbnail);

public sealed record KickChannelInfo(long BroadcasterUserId, string Slug, string? StreamTitle, KickCategory? Category, bool IsLive);

/// <summary>פרס נקודות בערוץ. CanManage=false אומר שהפרס נוצר מחוץ לתוכנה ואי אפשר לנהל אותו מכאן.</summary>
public sealed record KickReward(string Id, string Title, int Cost, bool IsEnabled, bool IsPaused, bool UserInputRequired);

/// <summary>צופה שקנה פרס (ממתין לאישור / דחייה).</summary>
public sealed record KickRedemption(string Id, string RewardId, long UserId, string UserInput, DateTime RedeemedAtUtc);

/// <summary>פעולות פרסי הנקודות (ממשק – כדי שאפשר יהיה לבדוק בלי קיק אמיתי).</summary>
public interface IKickRewardsApi
{
    bool IsConnected { get; }
    /// <summary>false = ההתחברות נעשתה לפני שהוספנו את הרשאת הפרסים – צריך להתחבר מחדש.</summary>
    bool HasRewardsScope { get; }
    Task<IReadOnlyList<KickReward>> GetRewardsAsync(CancellationToken ct);
    Task<KickReward> CreateRewardAsync(string title, int cost, string description, string color, CancellationToken ct);
    Task<KickReward> UpdateRewardAsync(string id, IReadOnlyDictionary<string, object> changes, CancellationToken ct);
    Task<IReadOnlyList<KickRedemption>> GetPendingRedemptionsAsync(string rewardId, CancellationToken ct);
    Task AcceptRedemptionsAsync(IReadOnlyList<string> ids, CancellationToken ct);
    Task RejectRedemptionsAsync(IReadOnlyList<string> ids, CancellationToken ct);
    Task<IReadOnlyDictionary<long, string>> GetUserNamesAsync(IReadOnlyList<long> userIds, CancellationToken ct);
}

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
