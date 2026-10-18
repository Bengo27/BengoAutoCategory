using System.Diagnostics;
using System.IO;
using System.Net.Http;
using AutoCategory.App.Platform;
using AutoCategory.Core.Models;
using AutoCategory.Core.Platforms;
using AutoCategory.Core.Platforms.Kick;
using AutoCategory.Core.Services;

namespace AutoCategory.App;

/// <summary>
/// "שורש ההרכבה": יוצר את כל השירותים פעם אחת ומחבר ביניהם.
/// הממשק מקבל את השירותים מכאן ולא יוצר אותם בעצמו.
/// </summary>
public sealed class AppServices : IDisposable
{
    public string DataDirectory { get; }
    public ILoggingService Log { get; }
    public ISettingsService Settings { get; }
    public ICategoryMappingService Mappings { get; }
    public IGameDetectionService Detector { get; }
    public SwitchingCategoryChangeService Changer { get; }
    public CategoryEngine Engine { get; }
    public DetectionCoordinator Coordinator { get; }
    public KickPlatform Kick { get; }
    public KickSyncService KickSync { get; }
    public AppConfig Config { get; }
    public UpdateService Updates { get; }

    /// <summary>שינויים אמיתיים בקיק: מחובר + "שינוי אמיתי" מופעל בהגדרות.</summary>
    public bool IsLive => Kick.IsConnected && Settings.Current.KickLiveMode;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public AppServices()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BengoAutoCategory");
        Directory.CreateDirectory(DataDirectory);
        SeedFromShippedData();

        var log = new LoggingService(Path.Combine(DataDirectory, "logs"));
        Log = log;
        Settings = new SettingsService(DataDirectory, log);
        log.SaveToFile = Settings.Current.SaveLogsToFile;

        Mappings = new CategoryMappingService(DataDirectory, log);
        Detector = new GameDetectionService(Mappings);

        // ── קיק ──
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BengoAutoCategory/" + (typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "0"));
        Config = AppConfigLoader.Load(DataDirectory);
        var kickStore = new KickAuthStore(DataDirectory, new DpapiSecretProtector());
        var kickApi = new KickApiClient(_http, kickStore)
        {
            // התחברות בלחיצה אחת דרך שרת ההתחברות (אם הוגדר בהפצה)
            BrokerUrl = Config.KickBrokerUrl,
            BrokerClientId = Config.KickClientId,
        };
        Kick = new KickPlatform(kickStore, kickApi, log, OpenInBrowser);
        Updates = new UpdateService(_http, Config, log);
        // מזהה קטגוריה שנמצא לפי שם נשמר בכלל – בפעם הבאה בלי חיפוש
        Kick.CategoryIdResolved += (name, id) => Mappings.SetKickCategoryId(name, id);
        Kick.FallbackCategory = () => Settings.Current.FallbackCategoryEnabled ? Settings.Current.FallbackCategory : null;

        // כשמחובר לקיק ו"שינוי אמיתי" מופעל – שינוי אמיתי; אחרת סימולציה
        Changer = new SwitchingCategoryChangeService(() => IsLive,
            new PlatformCategoryChangeService(Kick, log),
            new SimulatedCategoryChangeService(log));

        Engine = new CategoryEngine(Changer, log, () => Settings.Current, Settings.Current.CurrentCategory,
            c => Settings.Update(s => s.CurrentCategory = c));

        Coordinator = new DetectionCoordinator(
            new Win32ForegroundWindowService(), new FakeForegroundWindowService(),
            Detector, Engine, log, Settings);

        Coordinator.Mappings = Mappings;

        KickSync = new KickSyncService(Kick, Engine, log) { CanWrite = () => IsLive };
        Kick.ConnectionChanged += () => { if (Kick.IsConnected) _ = KickSync.SyncNowAsync(); };

        Settings.Changed += s => log.SaveToFile = s.SaveLogsToFile;
        Mappings.Changed += () => Coordinator.RequestImmediateDetection();
    }

    public void Start()
    {
        Coordinator.Start();
        KickSync.Start();
        Updates.StartPeriodicChecks();
        if (Kick.IsConnected)
            Log.Log(LogLevel.Info, $"מחובר לקיק בתור {Kick.AccountName} ({(Settings.Current.KickLiveMode ? "שינוי אמיתי" : "סימולציה")})");
    }

    public static void OpenInBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { DialogService.ShowError("לא ניתן לפתוח את הדפדפן:\n" + ex.Message + "\n\n" + url); }
    }

    /// <summary>
    /// בהפעלה הראשונה מעתיק את קבצי ה-JSON שמגיעים עם התוכנה (תיקיית Data) לתיקיית הנתונים של המשתמש.
    /// (התיקייה שליד ה-exe עלולה להיות בלי הרשאות כתיבה, למשל ב-Program Files.)
    /// </summary>
    private void SeedFromShippedData()
    {
        try
        {
            var shipped = Path.Combine(AppContext.BaseDirectory, "Data");
            foreach (var name in new[] { "game_mappings.json", "settings.json" })
            {
                var src = Path.Combine(shipped, name);
                var dst = Path.Combine(DataDirectory, name);
                if (File.Exists(src) && !File.Exists(dst)) File.Copy(src, dst);
            }
        }
        catch
        {
            // אם נכשל – השירותים ייצרו ברירות מחדל מהקוד
        }
    }

    public void Dispose()
    {
        KickSync.Dispose();
        Coordinator.Dispose();
    }
}
