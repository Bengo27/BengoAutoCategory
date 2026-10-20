using System.Text;
using System.Text.RegularExpressions;

namespace AutoCategory.Core.Tts;

/// <summary>
/// בודק ומנקה הודעת הקראה: מילים חסומות, קישורים, אורך, אימוג'ים של קיק ותווים חוזרים.
/// </summary>
public static class TtsFilter
{
    /// <summary>רשימת ברירת מחדל קצרה. הסטרימר יכול לערוך אותה בתוכנה.</summary>
    public static readonly IReadOnlyList<string> DefaultBannedWords = new[]
    {
        "זונה", "שרמוטה", "בן זונה", "כוסאמק", "כוס אמק", "כוס אמא שלך", "מזדיין", "תזדיין", "זין שלי",
        "fuck", "fucking", "motherfucker", "shit", "bitch", "cunt", "whore", "nigger", "nigga", "faggot", "retard",
    };

    public sealed record Result(bool Allowed, string Text, string? Reason);

    private static readonly Regex Emote = new(@"\[emote:\d+:[^\]]*\]", RegexOptions.Compiled);
    private static readonly Regex Link = new(
        @"(https?://|www\.)\S+|\b[\p{L}\p{N}-]+\.(com|net|org|io|gg|tv|co|me|ly|xyz|ru|il|app|link|site|live)\b(/\S*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    // אותו תו יותר מ-3 פעמים ברצף ("חחחחחחחח" → "חחח")
    private static readonly Regex Repeats = new(@"(.)\1{3,}", RegexOptions.Compiled);

    public static Result Check(string? input, TtsSettings s)
    {
        var text = input ?? "";
        text = Emote.Replace(text, " ");
        text = new string(text.Where(c => !char.IsControl(c)).ToArray());

        if (s.BlockLinks && Link.IsMatch(text))
            return new Result(false, text, "ההודעה מכילה קישור");

        text = Repeats.Replace(text, m => new string(m.Groups[1].Value[0], 3));
        text = Spaces.Replace(text, " ").Trim();

        if (!text.Any(char.IsLetterOrDigit))
            return new Result(false, text, "ההודעה ריקה");

        foreach (var word in s.BannedWords ?? new())
            if (ContainsWord(text, word))
                return new Result(false, text, "ההודעה מכילה מילה חסומה");

        if (text.Length > s.MaxChars)
            text = text[..s.MaxChars].TrimEnd() + "…";

        return new Result(true, text, null);
    }

    /// <summary>
    /// מילה שלמה, כולל אותיות שימוש בעברית לפניה (ו/ה/ש/ב/כ/ל/מ) – "והזונה" נתפס, "זונות" לא נתפס כ"זונה"... אלא אם ברשימה.
    /// </summary>
    public static bool ContainsWord(string text, string word)
    {
        word = word.Trim();
        if (word.Length == 0) return false;
        var pattern = @"(?<![\p{L}\p{N}])(?:[והשבכלמ]{0,2})" + Regex.Escape(Normalize(word)).Replace(@"\ ", @"\s+") + @"(?![\p{L}\p{N}])";
        return Regex.IsMatch(Normalize(text), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>מאחד אותיות סופיות וניקוד כדי שעקיפות פשוטות לא יעבדו.</summary>
    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue; // ניקוד
            sb.Append(c switch { 'ך' => 'כ', 'ם' => 'מ', 'ן' => 'נ', 'ף' => 'פ', 'ץ' => 'צ', _ => c });
        }
        return sb.ToString();
    }

    /// <summary>הטקסט שמוקרא בפועל לפי התבנית ("{user} אומר: {text}").</summary>
    public static string ApplyTemplate(string template, string user, string text) =>
        template.Replace("{user}", user).Replace("{text}", text).Trim();
}
