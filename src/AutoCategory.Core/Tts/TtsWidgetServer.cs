using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AutoCategory.Core.Models;
using AutoCategory.Core.Services;

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

/// <summary>
/// שרת HTTP קטן על המחשב בלבד (127.0.0.1) שמגיש את הווידג'ט ל-OBS.
/// בכוונה בלי HttpListener – כדי שלא יידרשו הרשאות מנהל.
/// </summary>
public sealed class TtsWidgetServer : IDisposable
{
    public const int DefaultPort = 47700;

    private readonly TtsWidgetHub _hub;
    private readonly ILoggingService _log;
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;

    public int Port { get; }
    public bool IsRunning => _listener is not null;
    public string WidgetUrl => $"http://localhost:{Port}/tts";

    public TtsWidgetServer(TtsWidgetHub hub, ILoggingService log, int port = DefaultPort)
    {
        _hub = hub;
        _log = log;
        Port = port;
    }

    public void Start()
    {
        if (_listener is not null) return;
        try
        {
            var l = new TcpListener(IPAddress.Loopback, Port);
            l.Start();
            _listener = l;
            _ = AcceptLoopAsync(l, _cts.Token);
        }
        catch (SocketException ex)
        {
            _log.Log(LogLevel.Warning, $"לא ניתן להפעיל את הווידג'ט ל-OBS (פורט {Port} תפוס)", ex.Message);
        }
    }

    private async Task AcceptLoopAsync(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await l.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch { break; }
            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 5000;
            var stream = client.GetStream();
            var (method, target) = await ReadRequestLineAsync(stream, ct).ConfigureAwait(false);
            if (method is null) return;

            var path = target;
            var query = "";
            var q = target.IndexOf('?');
            if (q >= 0) { path = target[..q]; query = target[(q + 1)..]; }
            string? Param(string name) =>
                query.Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0] == name)
                    .Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault();

            switch (method, path)
            {
                case ("GET", "/tts"):
                case ("GET", "/tts/"):
                    await WriteAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(TtsWidgetPage.Html), ct);
                    break;
                case ("GET", "/api/tts/state"):
                    await WriteAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(_hub.StateJson()), ct);
                    break;
                case ("GET", "/api/tts/audio"):
                    var audio = _hub.GetAudio(Param("key") ?? "");
                    if (audio is null) await WriteAsync(stream, 404, "text/plain", Encoding.UTF8.GetBytes("gone"), ct);
                    else await WriteAsync(stream, 200, audio.ContentType, audio.Data, ct);
                    break;
                case ("POST", "/api/tts/done"):
                    _hub.MarkDone(Param("key") ?? "");
                    await WriteAsync(stream, 204, "text/plain", Array.Empty<byte>(), ct);
                    break;
                default:
                    await WriteAsync(stream, 404, "text/plain", Encoding.UTF8.GetBytes("not found"), ct);
                    break;
            }
        }
        catch
        {
            // חיבור שנסגר באמצע – לא חשוב
        }
    }

    /// <summary>קורא את שורת הבקשה ואת הכותרות (עד 8KB) ומחזיר שיטה + נתיב.</summary>
    private static async Task<(string? Method, string Target)> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var total = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), timeout.Token).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n")) break;
        }
        var head = Encoding.ASCII.GetString(buffer, 0, total);
        var line = head.Split("\r\n", 2)[0].Split(' ');
        return line.Length >= 2 ? (line[0].ToUpperInvariant(), line[1]) : (null, "");
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string contentType, byte[] body, CancellationToken ct)
    {
        var reason = status switch { 200 => "OK", 204 => "No Content", 404 => "Not Found", _ => "Error" };
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n" +
                     "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct).ConfigureAwait(false);
        if (body.Length > 0) await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
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
