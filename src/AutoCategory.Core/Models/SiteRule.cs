using System.Text.RegularExpressions;

namespace AutoCategory.Core.Models;

/// <summary>
/// כלל אתר: אם הדפדפן בחזית וכותרת הלשונית מכילה את מילת המפתח → קטגוריה.
/// למשל "Stake" → "Slots & Casino".
/// </summary>
public sealed class SiteRule
{
    /// <summary>מילת מפתח בכותרת הלשונית (מילה שלמה, לא תלוי באותיות גדולות/קטנות).</summary>
    public string Keyword { get; set; } = "";
    public string Category { get; set; } = "";
    public string? KickCategoryId { get; set; }
    public string? Notes { get; set; }

    public bool Matches(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(Keyword) || string.IsNullOrEmpty(windowTitle)) return false;
        // מילה שלמה: "Stake" יתאים ל-"Stake.com | Sweet Bonanza" אבל לא ל-"Mistake"
        var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(Keyword.Trim())}(?![\p{{L}}\p{{N}}])";
        return Regex.IsMatch(windowTitle, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public SiteRule Clone() => new() { Keyword = Keyword, Category = Category, KickCategoryId = KickCategoryId, Notes = Notes };

    public bool SameKeyword(SiteRule other) =>
        string.Equals(Keyword.Trim(), other.Keyword.Trim(), StringComparison.OrdinalIgnoreCase);
}
