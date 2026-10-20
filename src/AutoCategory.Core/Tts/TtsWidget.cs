using System.Text.Json;
using AutoCategory.Core.Widgets;

namespace AutoCategory.Core.Tts;

/// <summary>
/// מה שהווידג'ט ב-OBS מציג עכשיו. הווידג'ט שואל כל חצי שנייה; אם הוא מנגן בעצמו – מדווח כשסיים.
/// </summary>
public sealed class TtsWidgetHub
{
    private sealed record Shown(string Key, string User, string Text, TtsAudio Audio, bool PlayHere, int Volume);

    private readonly object _lock = new();
    private readonly Func<DateTime> _utcNow;
    private Shown? _current;
    private TaskCompletionSource? _done;
    private long _seq;
    private DateTime _lastPollUtc = DateTime.MinValue;

    public TtsWidgetHub(Func<DateTime>? utcNow = null) => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>יש ווידג'ט פתוח ב-OBS (שאל בשניות האחרונות).</summary>
    public bool HasClient { get { lock (_lock) return _utcNow() - _lastPollUtc < TimeSpan.FromSeconds(6); } }

    /// <summary>מציג את ההודעה. אם playHere – מחכה שהווידג'ט יסיים לנגן.</summary>
    public async Task ShowAsync(TtsItem item, TtsAudio audio, bool playHere, int volume, CancellationToken ct)
    {
        TaskCompletionSource done;
        lock (_lock)
        {
            _current = new Shown($"{++_seq}", item.User, item.Text, audio, playHere, volume);
            _done = done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        if (!playHere) return;

        // ביטחון: אם הווידג'ט נסגר באמצע – לא נתקעים לנצח
        var timeout = audio.EstimatedDuration + TimeSpan.FromSeconds(15);
        using var reg = ct.Register(() => done.TrySetCanceled());
        var finished = await Task.WhenAny(done.Task, Task.Delay(timeout, CancellationToken.None)).ConfigureAwait(false);
        if (finished == done.Task) await done.Task.ConfigureAwait(false); // זורק ביטול (דילוג) אם היה
    }

    public void Clear()
    {
        lock (_lock)
        {
            _current = null;
            _done?.TrySetResult();
            _done = null;
        }
    }

    public void MarkDone(string key)
    {
        lock (_lock)
        {
            if (_current?.Key == key) _done?.TrySetResult();
        }
    }

    public string StateJson()
    {
        lock (_lock)
        {
            _lastPollUtc = _utcNow();
            var c = _current;
            return JsonSerializer.Serialize(c is null
                ? new Dictionary<string, object?> { ["key"] = null }
                : new Dictionary<string, object?>
                {
                    ["key"] = c.Key,
                    ["user"] = c.User,
                    ["text"] = c.Text,
                    ["play"] = c.PlayHere,
                    ["volume"] = c.Volume / 100.0,
                    ["audio"] = c.PlayHere ? "/api/tts/audio?key=" + c.Key : null,
                });
        }
    }

    public TtsAudio? GetAudio(string key)
    {
        lock (_lock) return _current?.Key == key ? _current.Audio : null;
    }
}

/// <summary>הנתיבים של ווידג'ט ההקראה בשרת הווידג'טים.</summary>
public static class TtsWidgetRoutes
{
    public const string Path = "/tts";

    public static void Map(WidgetServer server, TtsWidgetHub hub)
    {
        server.Map("GET", Path, _ => WidgetResponse.Html(TtsWidgetPage.Html));
        server.Map("GET", "/api/tts/state", _ => WidgetResponse.Json(hub.StateJson()));
        server.Map("GET", "/api/tts/audio", r =>
            hub.GetAudio(r.Param("key") ?? "") is { } audio ? WidgetResponse.Bytes(audio.ContentType, audio.Data) : WidgetResponse.NotFound);
        server.Map("POST", "/api/tts/done", r =>
        {
            hub.MarkDone(r.Param("key") ?? "");
            return WidgetResponse.NoContent;
        });
    }
}

/// <summary>דף הווידג'ט ל-OBS: רקע שקוף, כרטיס עם שם הצופה וההודעה.</summary>
internal static class TtsWidgetPage
{
    public const string Html = """
<!doctype html>
<html lang="he" dir="rtl">
<head>
<meta charset="utf-8">
<title>Catego TTS</title>
<style>
  html, body { margin: 0; background: transparent; overflow: hidden; }
  body { font-family: "Segoe UI", Arial, sans-serif; display: flex; align-items: flex-end; justify-content: center;
         height: 100vh; padding: 24px; box-sizing: border-box; }
  #card { max-width: 900px; background: rgba(17, 16, 24, .92); border-right: 6px solid #2DD4BF; border-radius: 14px;
          padding: 18px 26px; color: #fff; box-shadow: 0 10px 30px rgba(0,0,0,.45);
          opacity: 0; transform: translateY(30px); transition: opacity .35s ease, transform .35s ease; }
  #card.show { opacity: 1; transform: translateY(0); }
  #user { color: #2DD4BF; font-weight: 800; font-size: 26px; margin-bottom: 4px; }
  #user::before { content: "🔊 "; }
  #text { font-size: 30px; line-height: 1.35; font-weight: 600; word-wrap: break-word; }
</style>
</head>
<body>
<div id="card"><div id="user"></div><div id="text"></div></div>
<audio id="audio"></audio>
<script>
  const card = document.getElementById("card"), userEl = document.getElementById("user"),
        textEl = document.getElementById("text"), audio = document.getElementById("audio");
  const demo = new URLSearchParams(location.search).has("demo");
  let shown = null;

  function show(s) {
    userEl.textContent = s.user; textEl.textContent = s.text;
    card.classList.add("show");
  }
  function hide() { card.classList.remove("show"); audio.pause(); audio.removeAttribute("src"); }
  function done(key) { fetch("/api/tts/done?key=" + key, { method: "POST" }).catch(() => {}); }

  if (demo) {
    show({ user: "Bengo23", text: "ככה תיראה הודעת הקראה בשידור. אפשר להזיז ולשנות גודל ב-OBS." });
  } else {
    setInterval(async () => {
      let s;
      try { s = await (await fetch("/api/tts/state", { cache: "no-store" })).json(); } catch { return; }
      if (s.key === shown) return;
      shown = s.key;
      if (!s.key) { hide(); return; }
      show(s);
      if (s.play && s.audio) {
        const key = s.key;
        audio.src = s.audio;
        audio.volume = Math.max(0, Math.min(1, s.volume));
        audio.onended = () => done(key);
        audio.onerror = () => done(key);
        audio.play().catch(() => done(key));
      }
    }, 400);
  }
</script>
</body>
</html>
""";
}
