using AutoCategory.Core.Models;

namespace AutoCategory.Core.Storage;

/// <summary>מבנה הקובץ game_mappings.json.</summary>
public sealed class MappingDatabase
{
    public int Version { get; set; } = 1;
    public List<GameMapping> Mappings { get; set; } = new();
    public List<string> IgnoreList { get; set; } = new();
    /// <summary>כללי אתרים (נוסף בגרסה 2 של הקובץ).</summary>
    public List<SiteRule>? SiteRules { get; set; }
}

/// <summary>ערכי ברירת מחדל שנכתבים בהפעלה הראשונה.</summary>
public static class DefaultData
{
    public const int CurrentVersion = 2;

    /// <summary>דפדפנים – בהם בודקים את כללי האתרים לפי כותרת הלשונית.</summary>
    public static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "brave.exe", "vivaldi.exe", "arc.exe", "librewolf.exe",
    };

    public const string CasinoCategory = "Slots & Casino";

    public static List<SiteRule> CreateSiteRules() => new()
    {
        Site("Stake"), Site("Stake.us"), Site("Roobet"), Site("Rollbit"), Site("Shuffle.com"), Site("Gamdom"),
        Site("Duelbits"), Site("Rainbet"), Site("BC.Game"), Site("Thrill"), Site("Razed"), Site("Winna"),
    };

    private static SiteRule Site(string keyword) => new() { Keyword = keyword, Category = CasinoCategory };

    public static MappingDatabase CreateMappingDatabase() => new()
    {
        Version = CurrentVersion,
        SiteRules = CreateSiteRules(),
        Mappings = new List<GameMapping>
        {
            M("GTA5.exe", "Grand Theft Auto V"),
            M("GTA5_Enhanced.exe", "Grand Theft Auto V", notes: "GTA V Enhanced (גרסת 2025)"),
            M("VALORANT-Win64-Shipping.exe", "VALORANT"),
            M("cs2.exe", "Counter-Strike 2"),
            M("javaw.exe", "Minecraft", titleContains: "Minecraft", ambiguous: true,
              notes: "javaw.exe מריץ כל תוכנת Java – מתאים רק אם בכותרת החלון מופיע Minecraft"),
            M("Minecraft.Windows.exe", "Minecraft", notes: "Minecraft Bedrock"),
            M("League of Legends.exe", "League of Legends", notes: "בתוך המשחק"),
            M("LeagueClientUx.exe", "League of Legends", notes: "הלובי של League"),
            M("LeagueClient.exe", "League of Legends", notes: "הלובי של League"),
            M("FortniteClient-Win64-Shipping.exe", "Fortnite"),
            M("RocketLeague.exe", "Rocket League"),
            M("Overwatch.exe", "Overwatch 2"),
            M("Marvel-Win64-Shipping.exe", "Marvel Rivals", notes: "שם הקובץ טרם אומת על מחשב אמיתי"),
            M("r5apex.exe", "Apex Legends"),
            M("r5apex_dx12.exe", "Apex Legends"),
            M("dota2.exe", "Dota 2"),
            M("RobloxPlayerBeta.exe", "Roblox"),
            M("RustClient.exe", "Rust"),
        },
        IgnoreList = new List<string>
        {
            // כלי סטרימינג ותקשורת
            "obs64.exe", "obs32.exe", "Streamlabs OBS.exe", "StreamDeck.exe", "Discord.exe", "Spotify.exe", "claude.exe",
            // לאנצ'רים (לא המשחק עצמו)
            "steam.exe", "steamwebhelper.exe", "EpicGamesLauncher.exe", "Battle.net.exe",
            "RiotClientServices.exe", "RiotClientUx.exe", "EADesktop.exe", "MinecraftLauncher.exe",
            // דפדפנים (זיהוי דפדפן – בשלב מאוחר יותר)
            "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "brave.exe",
            // Windows עצמו
            "explorer.exe", "ShellExperienceHost.exe", "StartMenuExperienceHost.exe", "SearchHost.exe",
            "LockApp.exe", "Taskmgr.exe", "TextInputHost.exe", "ApplicationFrameHost.exe",
            // התוכנה הזו
            "Catego.exe", "BengoAutoCategory.exe",
        },
    };

    /// <summary>קבצים לבחירה במצב בדיקה.</summary>
    public static readonly IReadOnlyList<string> TestExecutables = new[]
    {
        "GTA5.exe",
        "VALORANT-Win64-Shipping.exe",
        "cs2.exe",
        "javaw.exe",
        "chrome.exe",
        "Discord.exe",
        "obs64.exe",
        "unknown-game.exe",
        "MyIndieGame.exe",
        "explorer.exe",
    };

    private static GameMapping M(string exe, string category, string? titleContains = null,
        bool ambiguous = false, string? notes = null) => new()
    {
        Executable = exe,
        Category = category,
        WindowTitleContains = titleContains,
        IsAmbiguous = ambiguous,
        Notes = notes,
    };
}
