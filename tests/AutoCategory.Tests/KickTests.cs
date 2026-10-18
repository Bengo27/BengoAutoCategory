using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoCategory.Core.Models;
using AutoCategory.Core.Platforms;
using AutoCategory.Core.Platforms.Kick;
using AutoCategory.Core.Services;

/// <summary>שרת קיק מדומה: עונה כמו ה-API הרשמי, ורושם כל בקשה כדי שנוכל לבדוק מה נשלח.</summary>
sealed class FakeKickServer : HttpMessageHandler
{
    public sealed record Req(HttpMethod Method, string Url, string? Auth, string Body);

    public List<Req> Requests { get; } = new();
    public string ValidAccessToken = "access-1";
    public string ValidRefreshToken = "refresh-1";
    public int TokenCounter = 1;
    public bool RefreshFails;
    public bool IsLive = true;
    public int BrokerCalls;
    public long ChannelCategoryId = 15;
    public string ChannelCategoryName = "Just Chatting";
    public Dictionary<string, long> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Just Chatting"] = 15, ["Grand Theft Auto V"] = 5, ["Counter-Strike 2"] = 7, ["Valorant"] = 9,
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var url = request.RequestUri!.ToString();
        var auth = request.Headers.Authorization?.Parameter;
        Requests.Add(new Req(request.Method, url, auth, body));

        if (url.StartsWith(KickEndpoints.Token))
        {
            var form = LoopbackCallbackListener.ParseQuery(body);
            if (form.GetValueOrDefault("client_id") != "cid" || form.GetValueOrDefault("client_secret") != "csecret")
                return Json(401, "{\"error\":\"invalid_client\"}");
            if (form["grant_type"] == "refresh_token")
            {
                if (RefreshFails || form["refresh_token"] != ValidRefreshToken) return Json(400, "{\"error\":\"invalid_grant\"}");
            }
            else if (form["grant_type"] != "authorization_code" || form.GetValueOrDefault("code") != "the-code" ||
                     string.IsNullOrEmpty(form.GetValueOrDefault("code_verifier")))
                return Json(400, "{\"error\":\"invalid_request\"}");

            TokenCounter++;
            ValidAccessToken = $"access-{TokenCounter}";
            ValidRefreshToken = $"refresh-{TokenCounter}";
            // expires_in כמחרוזת – כמו בדוגמה בתיעוד
            return Json(200, $"{{\"access_token\":\"{ValidAccessToken}\",\"token_type\":\"Bearer\",\"refresh_token\":\"{ValidRefreshToken}\",\"expires_in\":\"3600\",\"scope\":\"user:read channel:read channel:write\"}}");
        }

        if (url.StartsWith("https://broker.test/"))
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            BrokerCalls++;
            if (url.EndsWith("/token"))
            {
                if (r.GetProperty("code").GetString() != "the-code" || r.GetProperty("redirect_uri").GetString() is not { } ru || !ru.StartsWith("http://localhost:"))
                    return Json(400, "{\"error\":\"invalid_request\"}");
            }
            else if (r.GetProperty("refresh_token").GetString() != ValidRefreshToken)
                return Json(400, "{\"error\":\"invalid_grant\"}");
            TokenCounter++;
            ValidAccessToken = $"access-{TokenCounter}";
            ValidRefreshToken = $"refresh-{TokenCounter}";
            return Json(200, $"{{\"access_token\":\"{ValidAccessToken}\",\"refresh_token\":\"{ValidRefreshToken}\",\"expires_in\":3600}}");
        }

        if (url.StartsWith(KickEndpoints.Revoke)) return new HttpResponseMessage(HttpStatusCode.OK);

        if (auth != ValidAccessToken) return Json(401, "{\"message\":\"Unauthorized\"}");

        if (url.StartsWith(KickEndpoints.Users))
            return Json(200, "{\"data\":[{\"user_id\":123,\"name\":\"BengoTV\",\"email\":\"x@y.z\",\"profile_picture\":\"\"}],\"message\":\"OK\"}");

        if (url.StartsWith(KickEndpoints.Channels) && request.Method == HttpMethod.Get)
            return Json(200, $"{{\"data\":[{{\"broadcaster_user_id\":123,\"slug\":\"bengotv\",\"stream_title\":\"hi\",\"category\":{{\"id\":{ChannelCategoryId},\"name\":\"{ChannelCategoryName}\",\"thumbnail\":\"\"}},\"stream\":{{\"is_live\":{(IsLive ? "true" : "false")}}}}}],\"message\":\"OK\"}}");

        if (url.StartsWith(KickEndpoints.Channels) && request.Method == HttpMethod.Patch)
        {
            using var doc = JsonDocument.Parse(body);
            var id = doc.RootElement.GetProperty("category_id").GetInt64();
            ChannelCategoryId = id;
            ChannelCategoryName = Categories.FirstOrDefault(kv => kv.Value == id).Key ?? "?";
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (url.StartsWith(KickEndpoints.CategoriesV2) || url.StartsWith(KickEndpoints.CategoriesV1))
        {
            var q = LoopbackCallbackListener.ParseQuery(request.RequestUri.Query.TrimStart('?'));
            var term = q.GetValueOrDefault("name") ?? q.GetValueOrDefault("q") ?? "";
            var hits = Categories.Where(kv => kv.Key.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(kv => $"{{\"id\":{kv.Value},\"name\":\"{kv.Key}\",\"thumbnail\":\"\"}}");
            return Json(200, $"{{\"data\":[{string.Join(",", hits)}],\"message\":\"OK\",\"pagination\":{{\"next_cursor\":\"\"}}}}");
        }

        return Json(404, "{\"message\":\"not found\"}");
    }

    private static HttpResponseMessage Json(int status, string json) =>
        new((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>"הצפנה" הפיכה לבדיקות – מוודאת שהטוקנים לא נשמרים כטקסט גלוי.</summary>
sealed class ReverseProtector : ISecretProtector
{
    public string Protect(string plain) => "enc:" + new string(plain.Reverse().ToArray());
    public string Unprotect(string p) => new string(p[4..].Reverse().ToArray());
}

sealed class KickEnv : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "ac-kick-" + Guid.NewGuid().ToString("N"));
    public FakeKickServer Server { get; } = new();
    public DateTime UtcNow = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
    public LoggingService Log { get; }
    public KickAuthStore Store { get; }
    public KickApiClient Api { get; }
    public KickPlatform Kick { get; }
    public Func<string, Task>? Browser;

    public KickEnv(bool connected = true, int port = 0)
    {
        Directory.CreateDirectory(Dir);
        Log = new LoggingService(Path.Combine(Dir, "logs")) { SaveToFile = false };
        Store = new KickAuthStore(Dir, new ReverseProtector());
        Store.SetCredentials("cid", "csecret");
        Api = new KickApiClient(new HttpClient(Server), Store, () => UtcNow);
        Kick = new KickPlatform(Store, Api, Log, url => _ = Browser?.Invoke(url), () => UtcNow)
        {
            CallbackPort = port == 0 ? 47653 : port,
            RedirectUri = $"http://localhost:{(port == 0 ? 47653 : port)}/callback",
            LoginTimeout = TimeSpan.FromSeconds(10),
        };
        if (connected)
        {
            Store.SetTokens(new KickTokenResponse(Server.ValidAccessToken, Server.ValidRefreshToken, 3600, null), UtcNow);
            Store.SetAccount("BengoTV", "bengotv", 123, UtcNow);
        }
    }

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
}

static class KickTests
{
    public static void K01_Pkce_And_AuthorizeUrl()
    {
        var p = KickApiClient.CreatePkce();
        Assert.Equal(43, p.Verifier.Length, "verifier length");
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(p.Verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, p.Challenge, "S256 challenge");

        var url = KickApiClient.BuildAuthorizeUrl("cid", KickEndpoints.RedirectUri, KickEndpoints.Scopes, "st", p.Challenge);
        Assert.True(url.StartsWith("https://id.kick.com/oauth/authorize?"), "authorize host");
        foreach (var part in new[] { "response_type=code", "client_id=cid", "code_challenge_method=S256", "state=st",
                     "redirect_uri=http%3A%2F%2Flocalhost%3A47653%2Fcallback", "scope=user%3Aread%20channel%3Aread%20channel%3Awrite" })
            Assert.True(url.Contains(part), "missing " + part);
    }

    public static async Task K02_ExchangeCode_StoresEncryptedTokens()
    {
        using var k = new KickEnv(connected: false);
        await k.Api.ExchangeCodeAsync("the-code", "verifier", KickEndpoints.RedirectUri, default);
        var req = k.Server.Requests.Single();
        var form = LoopbackCallbackListener.ParseQuery(req.Body);
        Assert.Equal("authorization_code", form["grant_type"], "grant_type");
        Assert.Equal("verifier", form["code_verifier"], "code_verifier");
        Assert.Equal(KickEndpoints.RedirectUri, form["redirect_uri"], "redirect_uri");
        Assert.Equal("access-2", k.Store.AccessToken, "access token stored");

        var file = File.ReadAllText(k.Store.FilePath);
        Assert.True(!file.Contains("access-2") && !file.Contains("refresh-2") && !file.Contains("csecret"), "secrets not stored in plain text");
        Assert.True(new KickAuthStore(k.Dir, new ReverseProtector()).AccessToken == "access-2", "tokens survive restart");
    }

    public static async Task K03_SetCategory_SendsPatchWithId()
    {
        using var k = new KickEnv();
        var r = await k.Kick.SetCategoryAsync("Grand Theft Auto V", "5");
        Assert.True(r.Success, "success: " + r.Message);
        var patch = k.Server.Requests.Single(x => x.Method == HttpMethod.Patch);
        Assert.Equal(KickEndpoints.Channels, patch.Url, "PATCH url");
        Assert.Equal("{\"category_id\":5}", patch.Body, "PATCH body");
        Assert.Equal("access-1", patch.Auth, "bearer token");
    }

    public static async Task K04_SetCategory_ByName_ResolvesAndCaches()
    {
        using var k = new KickEnv();
        (string, long)? resolved = null;
        k.Kick.CategoryIdResolved += (n, id) => resolved = (n, id);
        var r = await k.Kick.SetCategoryAsync("counter-strike 2", null);
        Assert.True(r.Success, "success: " + r.Message);
        Assert.Equal(7L, k.Server.ChannelCategoryId, "changed to id 7");
        Assert.Equal(("counter-strike 2", 7L), resolved, "resolved event");

        var before = k.Server.Requests.Count;
        await k.Kick.SetCategoryAsync("counter-strike 2", null);
        Assert.Equal(before + 1, k.Server.Requests.Count, "second time: only PATCH (cached)");
    }

    public static async Task K05_UnknownCategory_FailsClearly()
    {
        using var k = new KickEnv();
        var r = await k.Kick.SetCategoryAsync("Some Made Up Game", null);
        Assert.True(!r.Success && r.Message.Contains("לא נמצאה"), "clear failure");
        Assert.True(!k.Server.Requests.Any(x => x.Method == HttpMethod.Patch), "no PATCH sent");
    }

    public static async Task K06_Expired401_RefreshesAndRetries()
    {
        using var k = new KickEnv();
        k.Server.ValidAccessToken = "access-rotated-on-server"; // הטוקן שלנו כבר לא תקף
        var r = await k.Kick.SetCategoryAsync("Valorant", "9");
        Assert.True(r.Success, "success after refresh: " + r.Message);
        var refresh = k.Server.Requests.Single(x => x.Url.StartsWith(KickEndpoints.Token));
        var form = LoopbackCallbackListener.ParseQuery(refresh.Body);
        Assert.Equal("refresh_token", form["grant_type"], "grant_type");
        Assert.Equal("refresh-1", form["refresh_token"], "old refresh token sent");
        Assert.Equal("refresh-2", k.Store.RefreshToken, "rotated refresh token stored");
    }

    public static async Task K07_ProactiveRefresh_WhenExpired()
    {
        using var k = new KickEnv();
        k.UtcNow = k.UtcNow.AddHours(2);
        k.Server.ValidAccessToken = "access-old-expired";
        await k.Kick.GetChannelAsync();
        Assert.True(k.Server.Requests[0].Url.StartsWith(KickEndpoints.Token), "refresh happened before the API call");
        Assert.Equal(2, k.Server.Requests.Count, "no wasted 401 round-trip");
    }

    public static async Task K08_RefreshFails_Disconnects()
    {
        using var k = new KickEnv();
        var changed = 0;
        k.Kick.ConnectionChanged += () => changed++;
        k.Server.ValidAccessToken = "x";
        k.Server.RefreshFails = true;
        var r = await k.Kick.SetCategoryAsync("Valorant", "9");
        Assert.True(!r.Success && r.Message.Contains("להתחבר מחדש"), "asks to reconnect: " + r.Message);
        Assert.True(!k.Kick.IsConnected, "marked disconnected");
        Assert.Equal(1, changed, "connection-changed raised");
    }

    public static async Task K09_LoopbackListener_ReceivesCallback()
    {
        var port = KickEnv.FreePort();
        using var listener = new LoopbackCallbackListener(port);
        listener.Start();
        var wait = listener.WaitForCallbackAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        using var http = new HttpClient();
        var fav = await http.GetAsync($"http://127.0.0.1:{port}/favicon.ico");
        Assert.Equal(HttpStatusCode.NotFound, fav.StatusCode, "favicon ignored");
        var page = await http.GetStringAsync($"http://127.0.0.1:{port}/callback?code=abc%2B1&state=xyz");
        var q = await wait;
        Assert.Equal("abc+1", q["code"], "code decoded");
        Assert.Equal("xyz", q["state"], "state");
        Assert.True(page.Contains("בהצלחה"), "success page in Hebrew");
    }

    public static async Task K10_ConnectAsync_FullFlow()
    {
        var port = KickEnv.FreePort();
        using var k = new KickEnv(connected: false, port: port);
        string? opened = null;
        k.Browser = async url =>
        {
            opened = url;
            // "הדפדפן": המשתמש מאשר, וקיק מחזיר אותו ל-redirect_uri עם code ו-state
            var q = LoopbackCallbackListener.ParseQuery(new Uri(url).Query.TrimStart('?'));
            await Task.Delay(100);
            using var http = new HttpClient();
            await http.GetStringAsync($"{q["redirect_uri"]}?code=the-code&state={Uri.EscapeDataString(q["state"])}");
        };
        var name = await k.Kick.ConnectAsync();
        Assert.Equal("BengoTV", name, "username");
        Assert.True(k.Kick.IsConnected, "connected");
        Assert.Equal("bengotv", k.Kick.ChannelSlug, "slug stored");
        Assert.True(opened!.Contains("code_challenge="), "PKCE used");
        var tokenReq = LoopbackCallbackListener.ParseQuery(k.Server.Requests.First(r => r.Url.StartsWith(KickEndpoints.Token)).Body);
        Assert.Equal($"http://localhost:{port}/callback", tokenReq["redirect_uri"], "same redirect uri in token exchange");
    }

    public static async Task K11_ConnectAsync_RejectsWrongState()
    {
        var port = KickEnv.FreePort();
        using var k = new KickEnv(connected: false, port: port);
        k.Browser = async url =>
        {
            var q = LoopbackCallbackListener.ParseQuery(new Uri(url).Query.TrimStart('?'));
            await Task.Delay(100);
            using var http = new HttpClient();
            await http.GetStringAsync($"{q["redirect_uri"]}?code=the-code&state=forged");
        };
        try { await k.Kick.ConnectAsync(); throw new Exception("expected failure"); }
        catch (KickException ex) { Assert.True(ex.Message.Contains("state"), "state mismatch detected"); }
        Assert.True(!k.Kick.IsConnected, "not connected");
    }

    public static async Task K12_ConnectAsync_UserDenied()
    {
        var port = KickEnv.FreePort();
        using var k = new KickEnv(connected: false, port: port);
        k.Browser = async url =>
        {
            var q = LoopbackCallbackListener.ParseQuery(new Uri(url).Query.TrimStart('?'));
            await Task.Delay(100);
            using var http = new HttpClient();
            await http.GetStringAsync($"{q["redirect_uri"]}?error=access_denied&state={q["state"]}");
        };
        try { await k.Kick.ConnectAsync(); throw new Exception("expected failure"); }
        catch (KickException ex) { Assert.True(ex.Message.Contains("access_denied"), "denial reported"); }
    }

    public static async Task K13_EndToEnd_GameDetected_ChangesKickCategory()
    {
        using var e = new Env();
        using var k = new KickEnv();
        k.Kick.CategoryIdResolved += (n, id) => e.Mappings.SetKickCategoryId(n, id);
        var live = new PlatformCategoryChangeService(k.Kick, e.Log);
        var engine = new CategoryEngine(live, e.Log, () => e.Settings.Current, "Just Chatting");
        var gta = e.Fg("GTA5.exe");
        for (int i = 0; i < 21; i++) { e.Now = e.Now.AddSeconds(1); await engine.UpdateAsync(gta, e.Now); }

        Assert.Equal("Grand Theft Auto V", engine.CurrentCategory, "engine category");
        Assert.Equal(5L, k.Server.ChannelCategoryId, "Kick channel category changed for real");
        Assert.Equal("5", e.Mappings.FindMapping("GTA5.exe", "")?.KickCategoryId, "resolved id saved into the rule");
        Assert.True(e.Log.Snapshot().Any(l => l.Level == LogLevel.Change && l.Message.Contains("קיק")), "logged as Kick change");
    }

    public static async Task K14_Sync_RespectsManualChangeOnKick()
    {
        using var e = new Env();
        using var k = new KickEnv();
        var live = new PlatformCategoryChangeService(k.Kick, e.Log);
        var engine = new CategoryEngine(live, e.Log, () => e.Settings.Current, "Just Chatting",
            c => e.Settings.Update(s => s.CurrentCategory = c));
        var sync = new KickSyncService(k.Kick, engine, e.Log);

        var gta = e.Fg("GTA5.exe");
        for (int i = 0; i < 21; i++) { e.Now = e.Now.AddSeconds(1); await engine.UpdateAsync(gta, e.Now); }
        Assert.True(!await sync.SyncNowAsync(), "our own change is not seen as manual");

        // הסטרימר שינה בעצמו באתר של קיק ל-Just Chatting, בזמן ש-GTA עדיין פתוח
        k.Server.ChannelCategoryId = 15; k.Server.ChannelCategoryName = "Just Chatting";
        Assert.True(await sync.SyncNowAsync(), "manual change detected");
        Assert.Equal("Just Chatting", engine.CurrentCategory, "engine follows Kick");
        Assert.Equal("Just Chatting", e.Settings.Current.CurrentCategory, "persisted");

        for (int i = 0; i < 60; i++) { e.Now = e.Now.AddSeconds(1); await engine.UpdateAsync(gta, e.Now); }
        Assert.Equal(15L, k.Server.ChannelCategoryId, "app does not fight the streamer's manual choice");
    }

    public static async Task K15_SwitchingService_SimulationWhenNotLive()
    {
        using var e = new Env();
        using var k = new KickEnv();
        var live = false;
        var svc = new SwitchingCategoryChangeService(() => live, new PlatformCategoryChangeService(k.Kick, e.Log),
            new SimulatedCategoryChangeService(e.Log));
        await svc.ChangeCategoryAsync("Just Chatting", "Valorant", null);
        Assert.True(!k.Server.Requests.Any(), "simulation: no request to Kick");
        live = true;
        await svc.ChangeCategoryAsync("Just Chatting", "Valorant", null);
        Assert.Equal(9L, k.Server.ChannelCategoryId, "live: Kick changed");
    }

    public static void K17_NameVariants()
    {
        var gta = KickApiClient.NameVariants("Grand Theft Auto V");
        foreach (var v in new[] { "GTA V", "GTA 5", "Grand Theft Auto 5" })
            Assert.True(gta.Contains(v), "GTA variants missing " + v + ": " + string.Join(" | ", gta));
        Assert.True(KickApiClient.NameVariants("Overwatch 2").Contains("Overwatch"), "Overwatch 2 -> Overwatch");
        Assert.True(KickApiClient.NameVariants("Some Game 3").Contains("Some Game III"), "3 -> III");
        Assert.Equal("Grand Theft Auto V", gta[0], "original first");
    }

    public static async Task K18_SmartMatch_FindsKickName()
    {
        using var k = new KickEnv();
        k.Server.Categories.Remove("Grand Theft Auto V");
        k.Server.Categories["GTA V"] = 50;
        k.Server.Categories["Overwatch"] = 60;

        var (gta, exact) = await k.Kick.FindBestCategoryAsync("Grand Theft Auto V");
        Assert.Equal(50L, gta?.Id, "GTA V found");
        Assert.True(!exact, "not exact");

        var r = await k.Kick.SetCategoryAsync("Overwatch 2", null);
        Assert.True(r.Success && r.AppliedCategory is null, "Overwatch matched: " + r.Message);
        Assert.Equal(60L, k.Server.ChannelCategoryId, "changed to Overwatch");
    }

    public static async Task K19_Fallback_GamesAndDemos()
    {
        using var e = new Env();
        using var k = new KickEnv();
        k.Server.Categories["Games + Demos"] = 99;
        k.Kick.FallbackCategory = () => "Games + Demos";
        var live = new PlatformCategoryChangeService(k.Kick, e.Log);

        var r = await live.ChangeCategoryAsync("Just Chatting", "Tiny Indie Demo", null);
        Assert.True(r.Success, "fallback success: " + r.Message);
        Assert.Equal("Games + Demos", r.AppliedCategory, "applied fallback");
        Assert.Equal(99L, k.Server.ChannelCategoryId, "Kick set to Games + Demos");
        Assert.True(e.Log.Snapshot().Any(l => l.Message.Contains("גיבוי")), "logged as fallback");

        k.Kick.FallbackCategory = () => null;
        var r2 = await k.Kick.SetCategoryAsync("Another Unknown Thing", null);
        Assert.True(!r2.Success, "fallback disabled -> fails");
    }

    public static async Task K20_Fallback_DoesNotTriggerManualSync()
    {
        using var e = new Env();
        using var k = new KickEnv();
        k.Server.Categories["Games + Demos"] = 99;
        k.Kick.FallbackCategory = () => "Games + Demos";
        e.Mappings.AddOrUpdate(new GameMapping { Executable = "indie.exe", Category = "Tiny Indie Demo" });
        var engine = new CategoryEngine(new PlatformCategoryChangeService(k.Kick, e.Log), e.Log, () => e.Settings.Current, "Just Chatting");
        var sync = new KickSyncService(k.Kick, engine, e.Log);
        var d = e.Fg("indie.exe");
        for (int i = 0; i < 21; i++) { e.Now = e.Now.AddSeconds(1); await engine.UpdateAsync(d, e.Now); }
        Assert.Equal(99L, k.Server.ChannelCategoryId, "Games + Demos on Kick");
        Assert.True(!await sync.SyncNowAsync(), "fallback is not seen as a manual change");
        Assert.Equal("Tiny Indie Demo", engine.CurrentCategory, "engine keeps the game");
    }

    public static async Task K21_StreamStart_PushesCorrectCategory()
    {
        using var e = new Env();
        using var k = new KickEnv();
        var engine = new CategoryEngine(new PlatformCategoryChangeService(k.Kick, e.Log), e.Log, () => e.Settings.Current, "Just Chatting");
        var sync = new KickSyncService(k.Kick, engine, e.Log);

        // לא בשידור: התוכנה משנה ל-Valorant בזמן שהסטרימר מתכונן
        k.Server.IsLive = false;
        await sync.SyncNowAsync();
        var val = e.Fg("VALORANT-Win64-Shipping.exe");
        for (int i = 0; i < 21; i++) { e.Now = e.Now.AddSeconds(1); await engine.UpdateAsync(val, e.Now); }
        Assert.Equal("VALORANT", engine.CurrentCategory, "engine on Valorant");

        // השידור מתחיל – וקיק מציג את הקטגוריה הישנה מהשידור הקודם
        k.Server.IsLive = true;
        k.Server.ChannelCategoryId = 15; k.Server.ChannelCategoryName = "Just Chatting";
        Assert.True(!await sync.SyncNowAsync(), "not treated as a manual change");
        Assert.Equal("VALORANT", engine.CurrentCategory, "engine still Valorant");
        Assert.Equal(9L, k.Server.ChannelCategoryId, "Valorant pushed to Kick at stream start");

        // מכאן – שינוי ידני באמצע שידור כן מכובד
        k.Server.ChannelCategoryId = 15; k.Server.ChannelCategoryName = "Just Chatting";
        Assert.True(await sync.SyncNowAsync(), "manual change mid-stream respected");
    }

    public static async Task K22_Offline_NoCategory_NoManualSync()
    {
        using var e = new Env();
        using var k = new KickEnv();
        k.Server.IsLive = false;
        var engine = new CategoryEngine(new PlatformCategoryChangeService(k.Kick, e.Log), e.Log, () => e.Settings.Current, "Grand Theft Auto V");
        var sync = new KickSyncService(k.Kick, engine, e.Log);
        Assert.True(!await sync.SyncNowAsync(), "offline: nothing to sync");
        Assert.Equal("Grand Theft Auto V", engine.CurrentCategory, "unchanged");
    }

    public static async Task K23_Broker_OneClickLogin_NoSecretInApp()
    {
        var port = KickEnv.FreePort();
        using var k = new KickEnv(connected: false, port: port);
        k.Store.ClearCredentials(); // סטרימר חדש – אין לו אפליקציה משלו
        k.Api.BrokerUrl = "https://broker.test";
        k.Api.BrokerClientId = "builtin-cid";
        Assert.True(k.Kick.HasCredentials && k.Kick.UsesBroker, "can connect via broker");

        string? opened = null;
        k.Browser = async url =>
        {
            opened = url;
            var q = LoopbackCallbackListener.ParseQuery(new Uri(url).Query.TrimStart('?'));
            await Task.Delay(100);
            using var http = new HttpClient();
            await http.GetStringAsync($"{q["redirect_uri"]}?code=the-code&state={Uri.EscapeDataString(q["state"])}");
        };
        await k.Kick.ConnectAsync();
        Assert.True(opened!.Contains("client_id=builtin-cid"), "built-in client id used");
        Assert.True(k.Kick.IsConnected, "connected via broker");
        Assert.Equal(1, k.Server.BrokerCalls, "token exchange went to the broker");
        Assert.True(!k.Server.Requests.Any(r => r.Url.StartsWith(KickEndpoints.Token)), "app never called Kick token URL directly");
        Assert.True(!k.Server.Requests.Any(r => r.Body.Contains("client_secret")), "no secret sent by the app");

        // רענון דרך השרת
        k.Server.ValidAccessToken = "rotated";
        var r = await k.Kick.SetCategoryAsync("Valorant", "9");
        Assert.True(r.Success, "refresh via broker then PATCH: " + r.Message);
        Assert.Equal(2, k.Server.BrokerCalls, "refresh went to the broker");
    }

    public static void K24_OwnAppOverridesBroker()
    {
        using var k = new KickEnv(connected: false);
        k.Api.BrokerUrl = "https://broker.test";
        k.Api.BrokerClientId = "builtin-cid";
        Assert.True(k.Kick.UsesOwnApp && !k.Kick.UsesBroker, "own credentials win");
        Assert.Equal("cid", k.Kick.EffectiveClientId, "own client id");
        k.Store.ClearCredentials();
        Assert.Equal("builtin-cid", k.Kick.EffectiveClientId, "back to built-in");
    }

    public static void K25_UpdateVersion()
    {
        Assert.True(UpdateVersion.IsNewer("v0.3.0", new Version(0, 2, 0)), "0.3.0 > 0.2.0");
        Assert.True(UpdateVersion.IsNewer("v0.2.1", new Version(0, 2, 0, 0)), "0.2.1 > 0.2.0.0");
        Assert.True(!UpdateVersion.IsNewer("v0.2.0", new Version(0, 2, 0, 0)), "same version");
        Assert.True(!UpdateVersion.IsNewer("v0.1.9", new Version(0, 2, 0)), "older");
        Assert.True(UpdateVersion.IsNewer("v1.0", new Version(0, 9, 9)), "1.0 > 0.9.9");
        Assert.True(!UpdateVersion.IsNewer("garbage", new Version(0, 2, 0)), "bad tag ignored");
        Assert.True(UpdateVersion.IsNewer("v0.10.0-beta", new Version(0, 9, 0)), "suffix ignored, numeric compare");
    }

    public static async Task K16_Disconnect_ClearsTokens()
    {
        using var k = new KickEnv();
        await k.Kick.DisconnectAsync();
        Assert.True(!k.Kick.IsConnected, "disconnected");
        Assert.True(k.Server.Requests.Any(r => r.Url.StartsWith(KickEndpoints.Revoke)), "token revoked on server");
        Assert.True(k.Store.HasCredentials, "client id/secret kept for easy reconnect");
    }
}
