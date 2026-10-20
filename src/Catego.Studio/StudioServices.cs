using System.Diagnostics;
using System.IO;
using System.Net.Http;
using AutoCategory.App;
using AutoCategory.App.Platform;
using AutoCategory.Core.Models;
using AutoCategory.Core.Platforms.Kick;
using AutoCategory.Core.Services;
using AutoCategory.Core.Storage;
using AutoCategory.Core.Tts;
using AutoCategory.Core.Widgets;
using Catego.Studio.Platform;

namespace Catego.Studio;

/// <summary>הגדרות כלליות של Studio (studio_settings.json).</summary>
public sealed class StudioSettings
{
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
}

/// <summary>"שורש ההרכבה" של Catego Studio: יוצר את כל השירותים פעם אחת.</summary>
public sealed class StudioServices : IDisposable
{
    public const string AppName = "Catego Studio";
    public const string ExeName = "CategoStudio.exe";
    public const string ReleaseTagPrefix = "studio-v";

    public string DataDirectory { get; }
    public ILoggingService Log { get; }
    public AppConfig Config { get; }
    public KickPlatform Kick { get; }
    public UpdateService Updates { get; }
    public TtsService Tts { get; }
    public WidgetServer Widgets { get; }
    public string TtsWidgetUrl => Widgets.BaseUrl + TtsWidgetRoutes.Path;

    private readonly string _settingsPath;
    private StudioSettings _settings;
    public event Action<StudioSettings>? SettingsChanged;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public StudioServices()
    {
        // תיקיית נתונים נפרדת מ-Catego (התחברות נפרדת לקיק, הגדרות נפרדות)
        DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CategoStudio");
        Directory.CreateDirectory(DataDirectory);

        var log = new LoggingService(Path.Combine(DataDirectory, "logs"));
        Log = log;

        _settingsPath = Path.Combine(DataDirectory, "studio_settings.json");
        try { _settings = JsonStore.Load<StudioSettings>(_settingsPath) ?? new StudioSettings(); }
        catch { JsonStore.BackupCorrupt(_settingsPath); _settings = new StudioSettings(); }

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CategoStudio/" + (typeof(StudioServices).Assembly.GetName().Version?.ToString(3) ?? "0"));
        Config = AppConfigLoader.Load(DataDirectory);

        // ── קיק (דרך שרת ההתחברות של Catego) ──
        var kickStore = new KickAuthStore(DataDirectory, new DpapiSecretProtector());
        var kickApi = new KickApiClient(_http, kickStore) { BrokerUrl = Config.KickBrokerUrl, BrokerClientId = Config.KickClientId };
        Kick = new KickPlatform(kickStore, kickApi, log, OpenInBrowser) { Scopes = KickEndpoints.StudioScopes, AppName = AppName };

        Updates = new UpdateService(_http, Config, log, ReleaseTagPrefix, ExeName);

        // ── הקראה (TTS) ──
        var hub = new TtsWidgetHub();
        Widgets = new WidgetServer(log);
        TtsWidgetRoutes.Map(Widgets, hub);
        var engine = new FallbackTtsEngine(new CloudTtsEngine(_http, () => Config.KickBrokerUrl), new SapiTtsEngine(), log);
        Tts = new TtsService(Kick, engine, new MediaTtsPlayer(), hub, new TtsSettingsStore(DataDirectory, log), log);
    }

    public StudioSettings Settings => new()
    {
        StartWithWindows = _settings.StartWithWindows,
        MinimizeToTray = _settings.MinimizeToTray,
        ShowNotifications = _settings.ShowNotifications,
    };

    public void SaveSettings(StudioSettings s)
    {
        _settings = s;
        try { JsonStore.Save(_settingsPath, s); } catch { }
        SettingsChanged?.Invoke(Settings);
    }

    public void Start()
    {
        Widgets.Start();
        Tts.Start();
        Updates.StartPeriodicChecks();
        Log.Log(LogLevel.Info, Kick.IsConnected ? $"{AppName} הופעל · מחובר לקיק בתור {Kick.AccountName}" : $"{AppName} הופעל");
    }

    public static void OpenInBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { DialogService.ShowError("לא ניתן לפתוח את הדפדפן:\n" + ex.Message + "\n\n" + url); }
    }

    public void Dispose()
    {
        Tts.Dispose();
        Widgets.Dispose();
    }
}
