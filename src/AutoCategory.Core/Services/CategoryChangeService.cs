using AutoCategory.Core.Models;

namespace AutoCategory.Core.Services;

/// <param name="AppliedCategory">הקטגוריה שהוגדרה בפועל, אם שונה מהמבוקשת (למשל קטגוריית גיבוי).</param>
public sealed record CategoryChangeResult(bool Success, string Message, string? AppliedCategory = null);

/// <summary>
/// השירות שמבצע בפועל את שינוי הקטגוריה.
/// שלב 1: SimulatedCategoryChangeService (רק רושם ליומן).
/// שלב 2: מימוש שמשתמש ב-IStreamingPlatform (למשל KickPlatform).
/// </summary>
public interface ICategoryChangeService
{
    string DisplayName { get; }
    Task<CategoryChangeResult> ChangeCategoryAsync(string fromCategory, string toCategory, GameMapping? mapping,
        CancellationToken cancellationToken = default);
}

/// <summary>מדמה שינוי קטגוריה – בלי שום קריאה לאינטרנט.</summary>
public sealed class SimulatedCategoryChangeService : ICategoryChangeService
{
    private readonly ILoggingService _log;

    public SimulatedCategoryChangeService(ILoggingService log) => _log = log;

    public string DisplayName => "סימולציה (ללא חיבור לקיק)";

    public Task<CategoryChangeResult> ChangeCategoryAsync(string fromCategory, string toCategory, GameMapping? mapping,
        CancellationToken cancellationToken = default)
    {
        _log.Log(LogLevel.Change, $"שינוי קטגוריה מדומה: מ-{fromCategory} ל-{toCategory}",
            $"SIMULATED CATEGORY CHANGE: {fromCategory} -> {toCategory}");
        return Task.FromResult(new CategoryChangeResult(true, "סימולציה"));
    }
}
