using AutoCategory.Core.Models;
using AutoCategory.Core.Services;
using AutoCategory.Core.Storage;

// מריץ בדיקות פשוט, בלי חבילות חיצוניות. קוד יציאה 0 = הכול עבר.
var runner = new TestRunner();
await runner.RunAllAsync(typeof(Tests));
await runner.RunAllAsync(typeof(KickTests));
runner.PrintSummary();
return runner.Failed == 0 ? 0 : 1;

// ───────────────────────────────────────────────────────────────────────────

sealed class TestRunner
{
    public int Passed, Failed;

    public async Task RunAllAsync(Type t)
    {
        foreach (var m in t.GetMethods().Where(m => m.IsStatic && m.DeclaringType == t && (m.Name[0] == 'T' || m.Name[0] == 'K') && m.Name.Contains('_')))
        {
            try
            {
                var r = m.Invoke(null, null);
                if (r is Task task) await task;
                Passed++;
                Console.WriteLine($"PASS  {m.Name}");
            }
            catch (Exception ex)
            {
                Failed++;
                var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                Console.WriteLine($"FAIL  {m.Name}: {inner.Message}");
            }
        }
    }

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine($"Total: {Passed + Failed}, Passed: {Passed}, Failed: {Failed}");
    }
}

static class Assert
{
    public static void True(bool cond, string msg) { if (!cond) throw new Exception(msg); }
    public static void Equal<T>(T expected, T actual, string what) =>
        True(EqualityComparer<T>.Default.Equals(expected, actual), $"{what}: expected '{expected}', got '{actual}'");
}

sealed class CountingChanger : ICategoryChangeService
{
    public List<(string From, string To)> Calls { get; } = new();
    public bool Fail { get; set; }
    public string DisplayName => "test";
    public Task<CategoryChangeResult> ChangeCategoryAsync(string from, string to, GameMapping? m, CancellationToken ct = default)
    {
        Calls.Add((from, to));
        return Task.FromResult(new CategoryChangeResult(!Fail, Fail ? "fail" : "ok"));
    }
}

/// <summary>סביבת בדיקה: תיקייה זמנית, שירותים אמיתיים, ושעון מדומה.</summary>
sealed class Env : IDisposable
{
    public string Dir { get; }
    public DateTime Now = new(2026, 10, 18, 12, 0, 0);
    public LoggingService Log { get; }
    public SettingsService Settings { get; }
    public CategoryMappingService Mappings { get; }
    public GameDetectionService Detector { get; }
    public CountingChanger Changer { get; } = new();
    public CategoryEngine Engine { get; private set; } = null!;

    public Env(string? dir = null, Action<AppSettings>? configure = null)
    {
        Dir = dir ?? Path.Combine(Path.GetTempPath(), "ac-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        Log = new LoggingService(Path.Combine(Dir, "logs"), () => Now) { SaveToFile = false };
        Settings = new SettingsService(Dir, Log);
        if (configure is not null) Settings.Update(configure);
        Mappings = new CategoryMappingService(Dir, Log);
        Detector = new GameDetectionService(Mappings);
        BuildEngine();
    }

    public void BuildEngine() =>
        Engine = new CategoryEngine(Changer, Log, () => Settings.Current, Settings.Current.CurrentCategory,
            c => Settings.Update(s => s.CurrentCategory = c));

    public DetectionResult Fg(string exe, string? title = null, params string[] background)
    {
        var fake = new FakeForegroundWindowService(() => Now);
        fake.SetForeground(exe, title);
        var running = background.Concat(string.IsNullOrEmpty(exe) ? Array.Empty<string>() : new[] { exe }).ToList();
        return Detector.Detect(fake.GetForeground(), running);
    }

    /// <summary>מריץ את המנוע שנייה-שנייה למשך N שניות עם אותה תוכנה בחזית.</summary>
    public async Task Run(string exe, int seconds, string? title = null, params string[] background)
    {
        var d = Fg(exe, title, background);
        for (int i = 0; i < seconds; i++)
        {
            Now = Now.AddSeconds(1);
            await Engine.UpdateAsync(d, Now);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch { }
    }
}

static class Tests
{
    // ── זיהוי ──

    public static void T01_KnownGame_ExactMatch_100()
    {
        using var e = new Env();
        var r = e.Fg("GTA5.exe");
        Assert.Equal(DetectionKind.Game, r.Kind, "kind");
        Assert.Equal("Grand Theft Auto V", r.Category, "category");
        Assert.Equal(100, r.Confidence, "confidence");
        var r2 = e.Fg("gta5.EXE"); // לא תלוי באותיות
        Assert.Equal(DetectionKind.Game, r2.Kind, "case-insensitive");
    }

    public static void T02_AmbiguousExecutable_80_OnlyWithTitle()
    {
        using var e = new Env();
        var mc = e.Fg("javaw.exe", "Minecraft 1.21.4");
        Assert.Equal(DetectionKind.Game, mc.Kind, "javaw+Minecraft kind");
        Assert.Equal(80, mc.Confidence, "javaw confidence");
        var other = e.Fg("javaw.exe", "IntelliJ IDEA");
        Assert.Equal(DetectionKind.Unknown, other.Kind, "javaw without Minecraft title must not be Minecraft");
    }

    public static void T03_IgnoreList_Defaults()
    {
        using var e = new Env();
        foreach (var exe in new[] { "obs64.exe", "Discord.exe", "explorer.exe", "Spotify.exe", "Steam.exe", "chrome.exe", "msedge.exe", "firefox.exe" })
            Assert.Equal(DetectionKind.Ignored, e.Fg(exe).Kind, exe);
    }

    public static void T04_UnknownApplication_0()
    {
        using var e = new Env();
        var r = e.Fg("eldenring.exe");
        Assert.Equal(DetectionKind.Unknown, r.Kind, "kind");
        Assert.Equal(0, r.Confidence, "confidence");
    }

    public static void T05_ManualMapping_PersistsAfterRestart()
    {
        using var e = new Env();
        e.Mappings.AddOrUpdate(new GameMapping { Executable = "eldenring.exe", Category = "Elden Ring" });
        Assert.Equal(DetectionKind.Game, e.Fg("eldenring.exe").Kind, "detected right away");

        var reloaded = new CategoryMappingService(e.Dir); // "הפעלה מחדש"
        Assert.Equal("Elden Ring", reloaded.FindMapping("eldenring.exe", "")?.Category, "after restart");
    }

    public static void T06_EditAndDeleteMapping_Persist()
    {
        using var e = new Env();
        var gta = e.Mappings.FindMapping("GTA5.exe", "")!;
        var edited = gta.Clone(); edited.Category = "GTA V";
        e.Mappings.Replace(gta, edited);
        Assert.Equal("GTA V", new CategoryMappingService(e.Dir).FindMapping("GTA5.exe", "")?.Category, "edit persisted");

        e.Mappings.Remove(edited);
        Assert.True(new CategoryMappingService(e.Dir).FindMapping("GTA5.exe", "") is null, "delete persisted");
    }

    public static void T07_IgnoreList_AddRemove_Persist()
    {
        using var e = new Env();
        e.Mappings.AddIgnored("MyTool");
        Assert.True(new CategoryMappingService(e.Dir).IsIgnored("mytool.exe"), "added + .exe appended");
        e.Mappings.RemoveIgnored("Discord.exe");
        Assert.True(!new CategoryMappingService(e.Dir).IsIgnored("Discord.exe"), "removed");
    }

    public static void T08_MappingBeatsIgnore()
    {
        using var e = new Env();
        e.Mappings.AddOrUpdate(new GameMapping { Executable = "chrome.exe", Category = "Browser Games" });
        Assert.Equal(DetectionKind.Game, e.Fg("chrome.exe").Kind, "explicit mapping wins over ignore");
        Assert.True(!e.Mappings.IsIgnored("chrome.exe"), "removed from ignore list");
    }

    public static void T09_Settings_PersistAndSanitize()
    {
        using var e = new Env();
        e.Settings.Update(s => { s.VerificationDelaySeconds = 45; s.DetectionIntervalSeconds = 999; s.ShowNotifications = false; });
        var reloaded = new SettingsService(e.Dir);
        Assert.Equal(45, reloaded.Current.VerificationDelaySeconds, "delay persisted");
        Assert.Equal(30, reloaded.Current.DetectionIntervalSeconds, "interval clamped");
        Assert.Equal(false, reloaded.Current.ShowNotifications, "bool persisted");
    }

    public static void T10_CorruptFiles_FallBackToDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ac-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ not json");
        File.WriteAllText(Path.Combine(dir, "game_mappings.json"), "garbage");
        using var e = new Env(dir);
        Assert.Equal(20, e.Settings.Current.VerificationDelaySeconds, "default settings");
        Assert.Equal(DetectionKind.Game, e.Fg("cs2.exe").Kind, "default mappings");
        Assert.True(Directory.GetFiles(dir, "*.corrupt-*").Length == 2, "backups of corrupt files kept");
    }

    // ── טיימר אימות ──

    public static async Task T11_Timer_ChangesOnlyAfter20Seconds()
    {
        using var e = new Env();
        await e.Run("GTA5.exe", 1); // מתחיל ספירה
        Assert.Equal(EnginePhase.Pending, e.Engine.State.Phase, "pending");
        Assert.Equal("Grand Theft Auto V", e.Engine.State.PendingCategory, "pending category");
        await e.Run("GTA5.exe", 19);
        Assert.Equal(0, e.Changer.Calls.Count, "no change before 20s");
        Assert.Equal(1, e.Engine.State.SecondsRemaining, "1 second left");
        await e.Run("GTA5.exe", 1);
        Assert.Equal(1, e.Changer.Calls.Count, "changed at 20s");
        Assert.Equal(("Just Chatting", "Grand Theft Auto V"), e.Changer.Calls[0], "from/to");
        Assert.Equal("Grand Theft Auto V", e.Engine.CurrentCategory, "current updated");
    }

    public static async Task T12_AltTabToIgnoredApp_PausesTimer_DoesNotReset()
    {
        using var e = new Env();
        await e.Run("GTA5.exe", 11);          // 10 שניות נספרו
        await e.Run("Discord.exe", 30);       // Alt-Tab ארוך לדיסקורד
        Assert.Equal(EnginePhase.PendingPaused, e.Engine.State.Phase, "paused while in Discord");
        Assert.Equal(0, e.Changer.Calls.Count, "no change while in Discord");
        await e.Run("GTA5.exe", 9);
        Assert.Equal(0, e.Changer.Calls.Count, "still not 20s of GTA");
        await e.Run("GTA5.exe", 1);
        Assert.Equal(1, e.Changer.Calls.Count, "changed after 20s of actual GTA time");
    }

    public static async Task T13_BriefAltTabToOtherGame_ReturnCancels()
    {
        using var e = new Env(configure: s => s.CurrentCategory = "Grand Theft Auto V");
        e.BuildEngine();
        await e.Run("cs2.exe", 10);
        Assert.Equal("Counter-Strike 2", e.Engine.State.PendingCategory, "cs2 pending");
        await e.Run("GTA5.exe", 1);
        Assert.Equal(null, e.Engine.State.PendingCategory, "returning to current game cancels");
        await e.Run("GTA5.exe", 60);
        Assert.Equal(0, e.Changer.Calls.Count, "no change at all");
    }

    public static async Task T14_DifferentGame_ReplacesPending()
    {
        using var e = new Env();
        await e.Run("GTA5.exe", 15);
        await e.Run("cs2.exe", 1);
        Assert.Equal("Counter-Strike 2", e.Engine.State.PendingCategory, "replaced");
        await e.Run("cs2.exe", 18);
        Assert.Equal(0, e.Changer.Calls.Count, "timer restarted for cs2");
        await e.Run("cs2.exe", 2);
        Assert.Equal(("Just Chatting", "Counter-Strike 2"), e.Changer.Calls.Single(), "changed to cs2");
    }

    public static async Task T15_OnlyIgnoredApps_NeverChange()
    {
        using var e = new Env();
        foreach (var exe in new[] { "obs64.exe", "Discord.exe", "steam.exe", "Spotify.exe", "explorer.exe", "chrome.exe" })
            await e.Run(exe, 60);
        Assert.Equal(0, e.Changer.Calls.Count, "never changed");
    }

    public static async Task T16_NoRepeatChangeToSameCategory()
    {
        using var e = new Env();
        await e.Run("GTA5.exe", 25);
        await e.Run("GTA5.exe", 300);
        await e.Run("Discord.exe", 10);
        await e.Run("GTA5.exe", 60);
        Assert.Equal(1, e.Changer.Calls.Count, "exactly one change");
        Assert.True(!await e.Engine.ChangeNowAsync(e.Now), "change-now with nothing pending does nothing");
        Assert.Equal(1, e.Changer.Calls.Count, "still one change");
    }

    // ── הגבלת קצב, מצב ידני, ביטול ──

    public static async Task T17_Cooldown_BetweenAutomaticChanges()
    {
        using var e = new Env(); // 120 שניות ברירת מחדל
        await e.Run("GTA5.exe", 21); // שנייה ראשונה = זיהוי, ואז 20 שניות אימות
        Assert.Equal(1, e.Changer.Calls.Count, "first change");
        await e.Run("cs2.exe", 25); // אומת אחרי 20 שניות, אבל רק 25 שניות עברו מהשינוי
        Assert.Equal(EnginePhase.Cooldown, e.Engine.State.Phase, "cooldown");
        Assert.Equal(1, e.Changer.Calls.Count, "blocked by cooldown");
        await e.Run("cs2.exe", 94);
        Assert.Equal(1, e.Changer.Calls.Count, "still blocked at 119s");
        await e.Run("cs2.exe", 1);
        Assert.Equal(2, e.Changer.Calls.Count, "allowed at 120s");
    }

    public static async Task T18_AutoOff_SuggestsOnly_ChangeNowWorks()
    {
        using var e = new Env(configure: s => s.AutoCategoryEnabled = false);
        await e.Run("GTA5.exe", 40);
        Assert.Equal(EnginePhase.AwaitingManual, e.Engine.State.Phase, "awaiting manual");
        Assert.Equal(0, e.Changer.Calls.Count, "no automatic change");
        Assert.True(await e.Engine.ChangeNowAsync(e.Now), "change now");
        Assert.Equal(1, e.Changer.Calls.Count, "manual change done");
        Assert.True(e.Log.Snapshot().Count > 0, "logged");
    }

    public static async Task T19_ChangeNow_SkipsTimer()
    {
        using var e = new Env();
        await e.Run("GTA5.exe", 3);
        Assert.True(await e.Engine.ChangeNowAsync(e.Now), "change now");
        Assert.Equal(1, e.Changer.Calls.Count, "changed immediately");
        await e.Run("GTA5.exe", 60);
        Assert.Equal(1, e.Changer.Calls.Count, "no duplicate");
    }

    public static async Task T20_Cancel_NotReSuggestedUntilGameChanges()
    {
        using var e = new Env();
        await e.Run("GTA5.exe", 10);
        e.Engine.Cancel(e.Now);
        await e.Run("GTA5.exe", 60);
        Assert.Equal(0, e.Changer.Calls.Count, "cancelled stays cancelled");
        Assert.Equal(null, e.Engine.State.PendingCategory, "no pending");
        await e.Run("Discord.exe", 5); // תוכנה מותעלמת לא "מאפסת" את הביטול
        await e.Run("GTA5.exe", 30);
        Assert.Equal(0, e.Changer.Calls.Count, "still cancelled after ignored app");
        await e.Run("cs2.exe", 2);
        await e.Run("GTA5.exe", 21);
        Assert.Equal(1, e.Changer.Calls.Count, "re-suggested after switching games");
    }

    // ── חזרה לברירת מחדל ומשחק ברקע ──

    public static async Task T21_ReturnToDefault_AfterNoGame()
    {
        using var e = new Env(configure: s => { s.CurrentCategory = "Grand Theft Auto V"; s.MinSecondsBetweenChanges = 0; });
        e.BuildEngine();
        await e.Run("explorer.exe", 5 * 60 - 1);
        Assert.Equal(0, e.Changer.Calls.Count, "not yet");
        Assert.Equal(EnginePhase.ReturningToDefault, e.Engine.State.Phase, "returning phase");
        await e.Run("explorer.exe", 2);
        Assert.Equal(("Grand Theft Auto V", "Just Chatting"), e.Changer.Calls.Single(), "returned to Just Chatting");
    }

    public static async Task T22_BackgroundGame_HoldsCategory_NeverStartsChange()
    {
        using var e = new Env(configure: s => s.CurrentCategory = "Grand Theft Auto V");
        e.BuildEngine();
        // GTA רץ ברקע, הסטרימר ב-OBS 20 דקות → לא חוזרים ל-Just Chatting
        await e.Run("obs64.exe", 20 * 60, null, "GTA5.exe");
        Assert.Equal(0, e.Changer.Calls.Count, "background game holds category");

        using var e2 = new Env();
        // cs2 רץ ברקע אבל לא בחזית → לא מתחילים שינוי
        await e2.Run("Discord.exe", 120, null, "cs2.exe");
        Assert.Equal(0, e2.Changer.Calls.Count, "background never starts a change");
    }

    public static async Task T23_Pause_CancelsPending()
    {
        using var e = new Env();
        await e.Run("GTA5.exe", 10);
        e.Engine.SetPaused(true);
        await e.Run("GTA5.exe", 60);
        Assert.Equal(0, e.Changer.Calls.Count, "no change while paused");
        e.Engine.SetPaused(false);
        await e.Run("GTA5.exe", 1);
        Assert.Equal(EnginePhase.Pending, e.Engine.State.Phase, "timer restarts from zero after resume");
        await e.Run("GTA5.exe", 20);
        Assert.Equal(1, e.Changer.Calls.Count, "changed after full delay");
    }

    public static async Task T24_SleepGap_IsClamped()
    {
        using var e = new Env();
        await e.Run("GTA5.exe", 1);
        e.Now = e.Now.AddHours(1); // המחשב נכנס לשינה
        await e.Engine.UpdateAsync(e.Fg("GTA5.exe"), e.Now);
        Assert.Equal(0, e.Changer.Calls.Count, "an hour of sleep does not count as 20s");
    }

    public static async Task T25_FailedChange_KeepsCategory()
    {
        using var e = new Env();
        e.Changer.Fail = true;
        await e.Run("GTA5.exe", 21);
        Assert.Equal("Just Chatting", e.Engine.CurrentCategory, "category unchanged on failure");
        Assert.True(e.Log.Snapshot().Any(l => l.Level == LogLevel.Warning), "warning logged");
    }

    // ── הפעלה מחדש ומצב בדיקה מקצה לקצה ──

    public static async Task T26_Restart_RestoresCurrentCategory()
    {
        using var e = new Env();
        await e.Run("GTA5.exe", 21);
        var restarted = new SettingsService(e.Dir);
        Assert.Equal("Grand Theft Auto V", restarted.Current.CurrentCategory, "current category persisted");
        e.BuildEngine();
        await e.Run("GTA5.exe", 60);
        Assert.Equal(1, e.Changer.Calls.Count, "no re-change after restart");
    }

    public static async Task T27_Coordinator_TestMode_EndToEnd()
    {
        using var e = new Env();
        var log = e.Log;
        var simulated = new SimulatedCategoryChangeService(log);
        var engine = new CategoryEngine(simulated, log, () => e.Settings.Current, "Just Chatting");
        var fake = new FakeForegroundWindowService(() => e.Now);
        var coord = new DetectionCoordinator(new FakeForegroundWindowService(() => e.Now), fake, e.Detector, engine, log, e.Settings)
        {
            TestMode = true,
        };
        fake.SetForeground("GTA5.exe");
        for (int i = 0; i < 25; i++) { e.Now = e.Now.AddSeconds(1); await coord.TickAsync(e.Now); }
        Assert.Equal("Grand Theft Auto V", engine.CurrentCategory, "changed via coordinator");
        Assert.True(log.Snapshot().Any(l => l.Detail?.Contains("SIMULATED CATEGORY CHANGE: Just Chatting -> Grand Theft Auto V") == true),
            "simulated change logged");

        fake.SetForeground("unknown-game.exe");
        for (int i = 0; i < 5; i++) { e.Now = e.Now.AddSeconds(1); await coord.TickAsync(e.Now); }
        Assert.Equal(DetectionKind.Unknown, coord.LastResult!.Kind, "unknown detected");
    }

    public static async Task T29_OwnWindow_KeepsDisplay_AndPausesTimer()
    {
        using var e = new Env();
        var engine = new CategoryEngine(e.Changer, e.Log, () => e.Settings.Current, "Just Chatting");
        var fake = new FakeForegroundWindowService(() => e.Now);
        var coord = new DetectionCoordinator(fake, new FakeForegroundWindowService(() => e.Now), e.Detector, engine, e.Log, e.Settings);
        DetectionResult? shown = null;
        coord.DetectionUpdated += d => shown = d;

        fake.SetForeground("GTA5.exe");
        for (int i = 0; i < 6; i++) { e.Now = e.Now.AddSeconds(1); await coord.TickAsync(e.Now); }

        fake.SetForeground("Catego.exe"); // הסטרימר מסתכל על לוח הבקרה
        for (int i = 0; i < 30; i++) { e.Now = e.Now.AddSeconds(1); await coord.TickAsync(e.Now); }
        Assert.Equal("GTA5.exe", shown?.Foreground.ExecutableName, "dashboard keeps showing the game");
        Assert.Equal(EnginePhase.PendingPaused, engine.State.Phase, "own window pauses the countdown");
        Assert.Equal(0, e.Changer.Calls.Count, "no change while looking at the dashboard");

        Assert.True(await engine.ChangeNowAsync(e.Now), "change-now works from the dashboard");
        Assert.Equal("Grand Theft Auto V", engine.CurrentCategory, "changed to the last game");
    }

    public static void T30_GameLibraryDetector()
    {
        var steam = GameLibraryDetector.Match(@"D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight.exe", "Hollow Knight");
        Assert.Equal("Steam", steam?.Library, "steam library");
        Assert.Equal("Hollow Knight", steam?.GameName, "name from title");

        var epic = GameLibraryDetector.Match(@"C:\Program Files\Epic Games\SomeGame\Binaries\Win64\SomeGame.exe", "");
        Assert.Equal("SomeGame", epic?.GameName, "name from folder when no title");

        Assert.True(GameLibraryDetector.Match(@"C:\Windows\System32\notepad.exe", "Untitled - Notepad") is null, "notepad is not a game");
        Assert.True(GameLibraryDetector.Match(@"D:\SteamLibrary\steamapps\common\Foo\UnityCrashHandler64.exe", "x") is null, "crash handler ignored");
        Assert.True(GameLibraryDetector.Match(@"D:\SteamLibrary\steamapps\common\Foo\FooLauncher.exe", "Foo") is null, "launcher ignored");

        var extra = GameLibraryDetector.Match(@"D:\Games\Old Game\game.exe", "", new[] { @"D:\Games" });
        Assert.Equal("Old Game", extra?.GameName, "extra folder");
        Assert.True(GameLibraryDetector.Match(@"D:\Games\Old Game\game.exe", "") is null, "extra folder not configured");
    }

    public static async Task T31_UnknownSteamGame_AutoAddedAndChanged()
    {
        using var e = new Env();
        var engine = new CategoryEngine(e.Changer, e.Log, () => e.Settings.Current, "Just Chatting");
        var fake = new FakeForegroundWindowService(() => e.Now);
        var coord = new DetectionCoordinator(fake, fake, e.Detector, engine, e.Log, e.Settings) { Mappings = e.Mappings };
        string? added = null;
        coord.GameAutoAdded += (m, _) => added = m.Category;

        fake.SetForeground("MyIndieGame.exe");
        for (int i = 0; i < 22; i++) { e.Now = e.Now.AddSeconds(1); await coord.TickAsync(e.Now); }
        Assert.Equal("My Indie Game", added, "rule auto-added");
        Assert.Equal("My Indie Game", e.Mappings.FindMapping("MyIndieGame.exe", "")?.Category, "rule persisted");
        Assert.Equal(("Just Chatting", "My Indie Game"), e.Changer.Calls.Single(), "category changed after 20s");

        // תוכנה רגילה לא נוספת
        fake.SetForeground("notepad.exe", "Untitled - Notepad", @"C:\Windows\System32\notepad.exe");
        for (int i = 0; i < 5; i++) { e.Now = e.Now.AddSeconds(1); await coord.TickAsync(e.Now); }
        Assert.True(e.Mappings.FindMapping("notepad.exe", "") is null, "notepad not auto-added");

        // הגדרה כבויה – לא מוסיפים
        e.Settings.Update(s => s.AutoDetectGames = false);
        fake.SetForeground("Other.exe", "Other", @"D:\SteamLibrary\steamapps\common\Other\Other.exe");
        for (int i = 0; i < 5; i++) { e.Now = e.Now.AddSeconds(1); await coord.TickAsync(e.Now); }
        Assert.True(e.Mappings.FindMapping("Other.exe", "") is null, "disabled: not added");
    }

    public static void T32_SiteRules_CasinoInBrowser()
    {
        using var e = new Env();
        var r = e.Fg("chrome.exe", "Stake.com | Sweet Bonanza - Google Chrome");
        Assert.Equal(DetectionKind.Game, r.Kind, "casino tab detected");
        Assert.Equal("Slots & Casino", r.Category, "category");
        Assert.Equal(90, r.Confidence, "site confidence");
        Assert.Equal("Stake", r.Site?.Keyword, "matched keyword");

        Assert.Equal("Stake.us", e.Fg("msedge.exe", "Stake.us - Social Casino - Microsoft Edge").Site?.Keyword, "longest keyword wins");
        Assert.Equal(DetectionKind.Ignored, e.Fg("chrome.exe", "YouTube - Google Chrome").Kind, "normal tab stays ignored");
        Assert.Equal(DetectionKind.Ignored, e.Fg("chrome.exe", "A common mistake - Google Chrome").Kind, "whole word only (mistake)");
        Assert.True(e.Fg("notepad.exe", "Stake notes.txt - Notepad").Site is null, "non-browser never uses site rules");
    }

    public static async Task T33_SiteRules_TimerAndAltTab()
    {
        using var e = new Env();
        await e.Run("chrome.exe", 21, "Roobet | Gates of Olympus - Google Chrome");
        Assert.Equal(("Just Chatting", "Slots & Casino"), e.Changer.Calls.Single(), "changed after 20s on casino tab");
        // מעבר ללשונית רגילה בדפדפן לא מחזיר ל-Just Chatting מיד ולא משנה כלום
        await e.Run("chrome.exe", 60, "Google - Google Chrome");
        Assert.Equal(1, e.Changer.Calls.Count, "normal tab is transparent");
    }

    public static void T34_SiteRules_CrudAndMigration()
    {
        using var e = new Env();
        e.Mappings.AddOrUpdateSite(new SiteRule { Keyword = "poki", Category = "Browser Games" });
        Assert.Equal("Browser Games", new CategoryMappingService(e.Dir).FindSiteRule("firefox.exe", "Poki - Free Online Games — Mozilla Firefox")?.Category, "persisted");
        var rule = e.Mappings.GetSiteRules().First(r => r.Keyword == "poki");
        e.Mappings.RemoveSite(rule);
        Assert.True(new CategoryMappingService(e.Dir).FindSiteRule("firefox.exe", "Poki - Mozilla Firefox") is null, "deleted");

        // קובץ ישן (גרסה 1, בלי siteRules) מקבל את כללי הקזינו אוטומטית
        var dir = Path.Combine(Path.GetTempPath(), "ac-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "game_mappings.json"),
            "{\"version\":1,\"mappings\":[{\"executable\":\"cs2.exe\",\"category\":\"Counter-Strike 2\"}],\"ignoreList\":[\"chrome.exe\"]}");
        var migrated = new CategoryMappingService(dir);
        Assert.Equal("Slots & Casino", migrated.FindSiteRule("chrome.exe", "Stake - Google Chrome")?.Category, "migration adds casino rules");
        Assert.Equal("Counter-Strike 2", migrated.FindMapping("cs2.exe", "")?.Category, "old rules kept");
        Assert.True(File.ReadAllText(Path.Combine(dir, "game_mappings.json")).Contains("siteRules"), "migration saved");
        Directory.Delete(dir, true);
    }

    public static void T35_RenameCategory_AllRuleTypes()
    {
        using var e = new Env();
        var n = e.Mappings.RenameCategory("Slots & Casino", "Slots & Casinos", "123");
        Assert.True(n >= 10, "site rules renamed: " + n);
        Assert.Equal("123", e.Mappings.FindSiteRule("chrome.exe", "Stake - Chrome")?.KickCategoryId, "kick id saved on site rule");
    }

    public static void T28_ExportDefaultMappingsFile()
    {
        // מייצר את קובץ ה-JSON שמצורף לפרויקט (Data/game_mappings.json)
        var outDir = Environment.GetEnvironmentVariable("AC_EXPORT_DIR");
        if (string.IsNullOrEmpty(outDir)) return;
        JsonStore.Save(Path.Combine(outDir, "game_mappings.json"), DefaultData.CreateMappingDatabase());
        JsonStore.Save(Path.Combine(outDir, "settings.json"), new AppSettings());
    }
}
