using System.Net.Http;
using System.Text;
using AutoCategory.Core.Models;
using AutoCategory.Core.Platforms.Kick;
using AutoCategory.Core.Services;
using AutoCategory.Core.Tts;

sealed class FakeEngine : ITtsEngine
{
    public List<TtsRequest> Requests { get; } = new();
    public bool Fail;
    public Task<TtsAudio> SynthesizeAsync(TtsRequest r, CancellationToken ct)
    {
        Requests.Add(r);
        if (Fail) throw new TtsException("boom");
        return Task.FromResult(new TtsAudio(new byte[600], r.Voice == TtsVoices.Windows ? "audio/wav" : "audio/mpeg"));
    }
}

sealed class FakePlayer : ITtsPlayer
{
    public int Played;
    /// <summary>true = מנגן "לנצח" עד ביטול (לבדיקת דילוג).</summary>
    public bool Block;
    public TaskCompletionSource Started = new();
    public async Task PlayAsync(TtsAudio audio, int volume, CancellationToken ct)
    {
        Played++;
        Started.TrySetResult();
        if (Block) await Task.Delay(Timeout.Infinite, ct);
    }
}

sealed class TtsEnv : IDisposable
{
    public KickEnv Kick { get; } = new();
    public FakeEngine Engine { get; } = new();
    public FakePlayer Player { get; } = new();
    public TtsWidgetHub Hub { get; }
    public TtsSettingsStore Settings { get; }
    public TtsService Tts { get; }

    public TtsEnv(bool enabled = true)
    {
        Hub = new TtsWidgetHub(() => Kick.UtcNow);
        Settings = new TtsSettingsStore(Kick.Dir);
        Settings.Update(s => { s.Enabled = enabled; s.GapSeconds = 0; });
        Tts = new TtsService(Kick.Kick, Engine, Player, Hub, Settings, Kick.Log, () => Kick.UtcNow);
    }

    public void Redeem(string id, long userId, string text, int minutesAgo = 0) =>
        Kick.Server.Pending.Add((id, Settings.Current.RewardId!, userId, text,
            Kick.UtcNow.AddMinutes(-minutesAgo).ToString("yyyy-MM-ddTHH:mm:ssZ")));

    public void Dispose() { Tts.Dispose(); Kick.Dispose(); }
}

static class TtsTests
{
    private static TtsSettings Defaults() { var s = new TtsSettings(); s.Sanitize(); return s; }

    public static void T40_Filter_BlocksLinksBannedWordsAndCleansText()
    {
        var s = Defaults();
        Assert.True(!TtsFilter.Check("תיכנסו ל-https://scam.example עכשיו", s).Allowed, "http link");
        Assert.True(!TtsFilter.Check("bit.ly/abc", s).Allowed, "short link");
        Assert.True(!TtsFilter.Check("אתה והזונה שלך", s).Allowed, "banned word with Hebrew prefix");
        Assert.True(!TtsFilter.Check("FUCK this", s).Allowed, "case-insensitive");
        Assert.True(TtsFilter.Check("shitake mushrooms", s).Allowed, "substring inside another word is fine");
        Assert.True(!TtsFilter.Check("[emote:123:KEKW]  ", s).Allowed, "emote-only message is empty");

        var r = TtsFilter.Check("חחחחחחחחח [emote:37226:KEKW] איזה   משחק", s);
        Assert.True(r.Allowed, "normal text allowed");
        Assert.Equal("חחח איזה משחק", r.Text, "repeats + emotes + spaces cleaned");

        s.MaxChars = 10;
        Assert.Equal("1234567890…", TtsFilter.Check("12345678901234", s).Text, "truncated");

        s.BlockLinks = false;
        Assert.True(TtsFilter.Check("www.google.com", s).Allowed, "links allowed when option off");

        Assert.Equal("דני אומר: היי", TtsFilter.ApplyTemplate("{user} אומר: {text}", "דני", "היי"), "template");
    }

    public static void T41_Settings_SanitizeAndPersist()
    {
        using var k = new KickEnv();
        var store = new TtsSettingsStore(k.Dir);
        store.Update(s => { s.RewardCost = 0; s.Voice = "nope"; s.MaxChars = 5000; s.Template = "no placeholder"; s.RatePercent = 99; });
        var again = new TtsSettingsStore(k.Dir).Current;
        Assert.Equal(1, again.RewardCost, "cost min 1");
        Assert.Equal(TtsVoices.Hila, again.Voice, "unknown voice → Hila");
        Assert.Equal(400, again.MaxChars, "max chars cap");
        Assert.True(again.Template.Contains("{text}"), "template restored");
        Assert.Equal(50, again.RatePercent, "rate clamp");
    }

    public static async Task T42_KickApi_RewardsAndRedemptions()
    {
        using var k = new KickEnv();
        var created = await k.Api.CreateRewardAsync("🔊 הקראה", 500, "d", "#2DD4BF", default);
        Assert.Equal(500, created.Cost, "cost");
        Assert.True(created.UserInputRequired, "input required");
        Assert.True(k.Server.Requests.Last().Body.Contains("\"is_user_input_required\":true"), "body asks for user input");

        var paused = await k.Api.UpdateRewardAsync(created.Id, new Dictionary<string, object> { ["is_paused"] = true }, default);
        Assert.True(paused.IsPaused, "paused");

        k.Server.Pending.Add(("R1", created.Id, 777, "שלום", "2026-10-18T09:59:00Z"));
        var list = await k.Api.GetPendingRedemptionsAsync(created.Id, default);
        Assert.Equal(1, list.Count, "one pending");
        Assert.Equal(777L, list[0].UserId, "user id");
        Assert.Equal("שלום", list[0].UserInput, "input");
        Assert.Equal(new DateTime(2026, 10, 18, 9, 59, 0, DateTimeKind.Utc), list[0].RedeemedAtUtc, "time");

        var names = await k.Api.GetUserNamesAsync(new long[] { 777 }, default);
        Assert.Equal("viewer777", names[777], "user name lookup");

        await k.Api.AcceptRedemptionsAsync(new[] { "R1" }, default);
        Assert.True(k.Server.Accepted.Contains("R1"), "accepted");
    }

    public static async Task T43_Tts_EnsureRewardCreatesThenUpdates()
    {
        using var e = new TtsEnv();
        var r = await e.Tts.EnsureRewardAsync();
        Assert.Equal(r.Id, e.Settings.Current.RewardId, "reward id saved");
        Assert.Equal(1, e.Kick.Server.Rewards.Count, "created once");

        e.Settings.Update(s => s.RewardCost = 900);
        await e.Tts.EnsureRewardAsync();
        Assert.Equal(1, e.Kick.Server.Rewards.Count, "updated, not duplicated");
        Assert.Equal(900, e.Kick.Server.Rewards[0].Cost, "new cost");

        // נמחק בקיק → נוצר מחדש
        e.Kick.Server.Rewards.Clear();
        await e.Tts.EnsureRewardAsync();
        Assert.Equal(1, e.Kick.Server.Rewards.Count, "recreated after delete");

        await e.Tts.SetEnabledAsync(false);
        Assert.True(e.Kick.Server.Rewards[0].Paused, "disable pauses the reward in Kick");
        await e.Tts.SetEnabledAsync(true);
        Assert.True(!e.Kick.Server.Rewards[0].Paused, "enable un-pauses");
    }

    public static async Task T44_Tts_ReadsAcceptsAndUsesName()
    {
        using var e = new TtsEnv();
        await e.Tts.EnsureRewardAsync();
        e.Redeem("R1", 42, "יאללה עוד סיבוב");

        Assert.Equal(1, await e.Tts.PollOnceAsync(default), "one new");
        Assert.Equal(0, await e.Tts.PollOnceAsync(default), "not added twice");
        var item = e.Tts.Items.Single();
        Assert.Equal("viewer42", item.User, "name from Kick");
        Assert.Equal(TtsStatus.Waiting, item.Status, "waiting");

        Assert.True(await e.Tts.PlayNextAsync(default), "played");
        Assert.Equal(1, e.Player.Played, "played in app");
        Assert.Equal("viewer42 אומר: יאללה עוד סיבוב", e.Engine.Requests[0].Text, "template text");
        Assert.Equal(TtsVoices.Hila, e.Engine.Requests[0].Voice, "voice");
        Assert.Equal(TtsStatus.Done, item.Status, "done");
        Assert.True(e.Kick.Server.Accepted.Contains("R1"), "accepted in Kick");
        Assert.True(!await e.Tts.PlayNextAsync(default), "queue empty");
    }

    public static async Task T45_Tts_RejectsBannedAndOldWithRefund()
    {
        using var e = new TtsEnv();
        await e.Tts.EnsureRewardAsync();
        e.Redeem("BAD", 1, "בן זונה");
        e.Redeem("OLD", 2, "הודעה ישנה", minutesAgo: 30);
        e.Redeem("LINK", 3, "www.scam.com");
        e.Redeem("OK", 4, "היי");
        await e.Tts.PollOnceAsync(default);

        foreach (var id in new[] { "BAD", "OLD", "LINK" })
            Assert.True(e.Kick.Server.Rejected.Contains(id), $"{id} rejected (points refunded)");
        Assert.True(!e.Kick.Server.Rejected.Contains("OK"), "ok not rejected");
        Assert.Equal(TtsStatus.Rejected, e.Tts.Items.First(i => i.Id == "BAD").Status, "status");

        await e.Tts.PlayNextAsync(default);
        Assert.Equal(1, e.Engine.Requests.Count, "only the good one is spoken");
    }

    public static async Task T46_Tts_ManualApproval()
    {
        using var e = new TtsEnv();
        e.Settings.Update(s => s.ManualApproval = true);
        await e.Tts.EnsureRewardAsync();
        e.Redeem("A", 1, "תאשר אותי");
        e.Redeem("B", 2, "ואותי לא");
        await e.Tts.PollOnceAsync(default);

        Assert.True(!await e.Tts.PlayNextAsync(default), "nothing plays before approval");
        var a = e.Tts.Items.First(i => i.Id == "A");
        Assert.Equal(TtsStatus.AwaitingApproval, a.Status, "awaiting");

        e.Tts.Approve(a);
        Assert.True(await e.Tts.PlayNextAsync(default), "plays after approval");
        await e.Tts.RejectAsync(e.Tts.Items.First(i => i.Id == "B"));
        Assert.True(e.Kick.Server.Rejected.Contains("B"), "manual reject refunds");
        Assert.True(!await e.Tts.PlayNextAsync(default), "rejected one never plays");
    }

    public static async Task T47_Tts_SkipStopsPlaybackAndAccepts()
    {
        using var e = new TtsEnv();
        e.Player.Block = true;
        await e.Tts.EnsureRewardAsync();
        e.Redeem("S", 1, "הודעה ארוכה מאוד");
        await e.Tts.PollOnceAsync(default);

        var play = e.Tts.PlayNextAsync(default);
        await e.Player.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TtsStatus.Playing, e.Tts.Current?.Status, "playing");
        e.Tts.SkipCurrent();
        await play.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TtsStatus.Skipped, e.Tts.Items.Single().Status, "skipped");
        Assert.True(e.Kick.Server.Accepted.Contains("S"), "skipped still counts as used");
    }

    public static async Task T48_Tts_EngineFailureRefunds()
    {
        using var e = new TtsEnv();
        e.Engine.Fail = true;
        await e.Tts.EnsureRewardAsync();
        e.Redeem("F", 1, "היי");
        await e.Tts.PollOnceAsync(default);
        await e.Tts.PlayNextAsync(default);
        Assert.Equal(TtsStatus.Failed, e.Tts.Items.Single().Status, "failed");
        Assert.True(e.Kick.Server.Rejected.Contains("F"), "refunded on failure");
    }

    public static async Task T49_Tts_DisabledAndPausedQueue()
    {
        using var e = new TtsEnv(enabled: false);
        await e.Tts.EnsureRewardAsync();
        Assert.True(e.Kick.Server.Rewards[0].Paused, "new reward is paused while TTS is off");
        e.Redeem("X", 1, "היי");
        Assert.Equal(0, await e.Tts.PollOnceAsync(default), "no polling while disabled");

        e.Tts.EnqueueTest("בדיקה אחת שתיים");
        Assert.True(await e.Tts.PlayNextAsync(default), "voice test works even when off");
        Assert.Equal(0, e.Kick.Server.Accepted.Count + e.Kick.Server.Rejected.Count, "test never touches Kick");

        await e.Tts.SetEnabledAsync(true);
        e.Tts.QueuePaused = true;
        await e.Tts.PollOnceAsync(default);
        Assert.True(!await e.Tts.PlayNextAsync(default), "paused queue holds messages");
        e.Tts.QueuePaused = false;
        Assert.True(await e.Tts.PlayNextAsync(default), "resumes");
    }

    public static async Task T50_Tts_NeedsRewardsScope()
    {
        using var e = new TtsEnv();
        // התחברות ישנה: רק ההרשאות של הקטגוריה
        e.Kick.Store.SetTokens(new KickTokenResponse(e.Kick.Server.ValidAccessToken, e.Kick.Server.ValidRefreshToken, 3600,
            "user:read channel:read channel:write"), e.Kick.UtcNow);
        Assert.True(!e.Kick.Kick.HasRewardsScope, "old login has no rewards scope");
        try
        {
            await e.Tts.EnsureRewardAsync();
            throw new Exception("should have thrown");
        }
        catch (KickException ex)
        {
            Assert.True(ex.Message.Contains("התחבר"), "asks to reconnect");
        }
        Assert.True(KickEndpoints.Scopes.Contains("channel:rewards:write"), "new logins ask for rewards");
    }

    public static async Task T51_FallbackEngine_UsesWindowsVoiceWhenCloudFails()
    {
        var cloud = new FakeEngine { Fail = true };
        var local = new FakeEngine();
        var log = new LoggingService(Path.Combine(Path.GetTempPath(), "ac-tts-log")) { SaveToFile = false };
        var engine = new FallbackTtsEngine(cloud, local, log);

        var audio = await engine.SynthesizeAsync(new TtsRequest("היי", TtsVoices.Avri, 0), default);
        Assert.Equal(1, cloud.Requests.Count, "tried cloud first");
        Assert.Equal(TtsVoices.Windows, local.Requests.Single().Voice, "fell back to Windows voice");
        Assert.Equal(".wav", audio.Extension, "wav from Windows voice");

        await engine.SynthesizeAsync(new TtsRequest("היי", TtsVoices.Windows, 0), default);
        Assert.Equal(1, cloud.Requests.Count, "Windows voice never calls the server");
    }

    public static async Task T52_CloudEngine_CallsServer()
    {
        var handler = new RecordingHandler();
        var engine = new CloudTtsEngine(new HttpClient(handler), () => "https://broker.test/");
        var audio = await engine.SynthesizeAsync(new TtsRequest("שלום", TtsVoices.Avri, -10), default);
        Assert.Equal("https://broker.test/tts", handler.Url, "url");
        Assert.True(handler.Body.Contains("\"voice\":\"he-IL-AvriNeural\"") && handler.Body.Contains("\"rate\":-10"), "body");
        Assert.Equal(".mp3", audio.Extension, "mp3");

        handler.Status = 429;
        try { await engine.SynthesizeAsync(new TtsRequest("x", TtsVoices.Avri, 0), default); throw new Exception("no throw"); }
        catch (TtsException ex) { Assert.True(ex.Message.Contains("מכסה"), "quota message"); }
    }

    public static async Task T53_WidgetServer_ServesPageStateAndPlayback()
    {
        using var k = new KickEnv();
        var hub = new TtsWidgetHub();
        var port = KickEnv.FreePort();
        using var server = new TtsWidgetServer(hub, k.Log, port);
        server.Start();
        Assert.True(server.IsRunning, "running");
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        var page = await http.GetStringAsync("/tts");
        Assert.True(page.Contains("/api/tts/state") && page.Contains("dir=\"rtl\""), "widget page");
        Assert.True((await http.GetStringAsync("/api/tts/state")).Contains("\"key\":null"), "empty state");
        Assert.True(hub.HasClient, "poll marks client as connected");

        var item = new TtsItem { Id = "1", User = "דני", Text = "היי", ReceivedAtUtc = DateTime.UtcNow };
        var audio = new TtsAudio(Encoding.ASCII.GetBytes(new string('x', 6000)), "audio/mpeg");
        var show = hub.ShowAsync(item, audio, playHere: true, 70, default);

        var state = await http.GetStringAsync("/api/tts/state");
        Assert.True(state.Contains("דני") || state.Contains("\\u05D3"), "state has user");
        var key = System.Text.Json.JsonDocument.Parse(state).RootElement.GetProperty("key").GetString()!;
        var bytes = await http.GetByteArrayAsync($"/api/tts/audio?key={key}");
        Assert.Equal(6000, bytes.Length, "audio served");
        Assert.True(!show.IsCompleted, "waits for the widget to finish");

        (await http.PostAsync($"/api/tts/done?key={key}", null)).EnsureSuccessStatusCode();
        await show.WaitAsync(TimeSpan.FromSeconds(5));
        hub.Clear();
        Assert.True((await http.GetStringAsync("/api/tts/state")).Contains("\"key\":null"), "cleared");
    }

    public static async Task T54_Tts_WidgetPlaybackFallsBackToAppWithoutOBS()
    {
        using var e = new TtsEnv();
        e.Settings.Update(s => s.Playback = TtsPlayback.Widget);
        await e.Tts.EnsureRewardAsync();
        e.Redeem("W", 1, "היי");
        await e.Tts.PollOnceAsync(default);
        await e.Tts.PlayNextAsync(default);
        Assert.Equal(1, e.Player.Played, "no widget open → played in app instead of getting stuck");
    }
}

sealed class RecordingHandler : HttpMessageHandler
{
    public string Url = "", Body = "";
    public int Status = 200;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Url = request.RequestUri!.ToString();
        Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var res = new HttpResponseMessage((System.Net.HttpStatusCode)Status)
        {
            Content = new ByteArrayContent(new byte[500]),
        };
        res.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
        return res;
    }
}
