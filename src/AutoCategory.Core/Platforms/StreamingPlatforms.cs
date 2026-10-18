using AutoCategory.Core.Models;
using AutoCategory.Core.Services;

namespace AutoCategory.Core.Platforms;

/// <summary>
/// ממשק לפלטפורמת סטרימינג (Kick, Twitch...).
///
/// זרימה:
///   Detector → CategoryEngine → ICategoryChangeService (PlatformCategoryChangeService) → IStreamingPlatform → Kick / Twitch
/// </summary>
public interface IStreamingPlatform
{
    string Name { get; }
    bool IsConnected { get; }

    /// <summary>שם המשתמש המחובר (אם יש).</summary>
    string? AccountName { get; }

    /// <summary>מחזיר את הקטגוריה הנוכחית של הערוץ.</summary>
    Task<string?> GetCurrentCategoryAsync(CancellationToken cancellationToken = default);

    /// <summary>משנה את קטגוריית הערוץ. platformCategoryId – אם ידוע (למשל category_id של Kick).</summary>
    Task<CategoryChangeResult> SetCategoryAsync(string categoryName, string? platformCategoryId,
        CancellationToken cancellationToken = default);
}

/// <summary>מקום שמור ל-Twitch. לא ממומש.</summary>
public sealed class TwitchPlatform : IStreamingPlatform
{
    public string Name => "Twitch";
    public bool IsConnected => false;
    public string? AccountName => null;

    public Task<string?> GetCurrentCategoryAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Twitch לא ממומש עדיין.");

    public Task<CategoryChangeResult> SetCategoryAsync(string categoryName, string? platformCategoryId,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Twitch לא ממומש עדיין.");
}

/// <summary>מתאם: מחבר את מנוע הקטגוריות לפלטפורמה אמיתית.</summary>
public sealed class PlatformCategoryChangeService : ICategoryChangeService
{
    private readonly IStreamingPlatform _platform;
    private readonly ILoggingService _log;

    public PlatformCategoryChangeService(IStreamingPlatform platform, ILoggingService log)
    {
        _platform = platform;
        _log = log;
    }

    public string DisplayName => _platform.Name;

    public async Task<CategoryChangeResult> ChangeCategoryAsync(string fromCategory, string toCategory, GameMapping? mapping,
        CancellationToken cancellationToken = default)
    {
        var result = await _platform.SetCategoryAsync(toCategory, mapping?.KickCategoryId, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return result;

        var applied = result.AppliedCategory;
        if (string.IsNullOrWhiteSpace(applied) || string.Equals(applied, toCategory, StringComparison.OrdinalIgnoreCase))
            _log.Log(LogLevel.Change, $"הקטגוריה ב{_platform.Name} שונתה: מ-{fromCategory} ל-{toCategory}",
                $"{_platform.Name.ToUpperInvariant()} CATEGORY CHANGE: {fromCategory} -> {toCategory}");
        else
            _log.Log(LogLevel.Change, $"הקטגוריה ב{_platform.Name} שונתה ל-{applied} (קטגוריית גיבוי – \"{toCategory}\" לא קיימת ב{_platform.Name})",
                $"{_platform.Name.ToUpperInvariant()} CATEGORY CHANGE: {fromCategory} -> {applied} (fallback for {toCategory})");
        return result;
    }
}
