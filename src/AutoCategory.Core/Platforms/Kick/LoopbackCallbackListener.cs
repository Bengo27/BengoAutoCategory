using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AutoCategory.Core.Platforms.Kick;

/// <summary>
/// שרת מקומי זעיר (רק על המחשב הזה – localhost) שמקבל את החזרה מדף ההתחברות של קיק.
/// מבוסס TcpListener ולכן לא צריך הרשאות מנהל, ולא פותח פורט לרשת.
/// </summary>
public sealed class LoopbackCallbackListener : IDisposable
{
    private readonly int _port;
    private readonly string _path;
    private readonly List<TcpListener> _listeners = new();

    public LoopbackCallbackListener(int port, string path = "/callback")
    {
        _port = port;
        _path = path;
    }

    public void Start()
    {
        // הדפדפן עשוי לפנות ל-localhost ב-IPv4 או ב-IPv6 – מאזינים לשניהם
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            try
            {
                var l = new TcpListener(address, _port);
                l.Start();
                _listeners.Add(l);
            }
            catch (SocketException)
            {
                // למשל IPv6 כבוי – מספיק שאחד עובד
            }
        }
        if (_listeners.Count == 0)
            throw new KickException($"לא ניתן לפתוח את הפורט המקומי {_port}. ייתכן שתוכנה אחרת משתמשת בו.");
    }

    /// <summary>מחכה לבקשה לנתיב ה-callback ומחזיר את פרמטרי ה-query שלה.</summary>
    public async Task<IReadOnlyDictionary<string, string>> WaitForCallbackAsync(CancellationToken ct)
    {
        using var reg = ct.Register(Stop);
        // משימת "קבלת חיבור" אחת קבועה לכל מאזין – כך שאף חיבור לא "נבלע" על ידי משימה נטושה
        var pending = _listeners.Select(l => l.AcceptTcpClientAsync(ct).AsTask()).ToArray();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            TcpClient client;
            try
            {
                var done = await Task.WhenAny(pending).ConfigureAwait(false);
                var idx = Array.IndexOf(pending, done);
                client = await done.ConfigureAwait(false);
                pending[idx] = _listeners[idx].AcceptTcpClientAsync(ct).AsTask();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            using (client)
            {
                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();
                var requestLine = await ReadRequestLineAsync(stream, ct).ConfigureAwait(false);
                // דוגמה: GET /callback?code=...&state=... HTTP/1.1
                var parts = requestLine.Split(' ');
                var target = parts.Length >= 2 ? parts[1] : "";
                var q = target.IndexOf('?');
                var path = q >= 0 ? target[..q] : target;

                if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 404, "Not found", ct).ConfigureAwait(false);
                    continue; // למשל favicon.ico
                }

                var query = ParseQuery(q >= 0 ? target[(q + 1)..] : "");
                var ok = query.ContainsKey("code");
                await WriteResponseAsync(stream, 200, ok ? SuccessPage : ErrorPage(query), ct).ConfigureAwait(false);
                return query;
            }
        }
    }

    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((i >= 0 ? pair[..i] : pair).Replace('+', ' '));
            var value = i >= 0 ? Uri.UnescapeDataString(pair[(i + 1)..].Replace('+', ' ')) : "";
            result[key] = value;
        }
        return result;
    }

    private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        // קוראים את כותרות הבקשה (עד שורה ריקה) ומחזירים את השורה הראשונה
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        while (sb.Length < 65536)
        {
            var n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0) break;
            sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
            if (sb.ToString().Contains("\r\n\r\n", StringComparison.Ordinal)) break;
        }
        var text = sb.ToString();
        var end = text.IndexOf("\r\n", StringComparison.Ordinal);
        return end >= 0 ? text[..end] : text;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string html, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var header = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\n" +
                     "Content-Type: text/html; charset=utf-8\r\n" +
                     $"Content-Length: {body.Length}\r\n" +
                     "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private const string PageStyle =
        "<style>body{background:#0E0F13;color:#E8EAED;font-family:'Segoe UI',Arial,sans-serif;display:flex;" +
        "align-items:center;justify-content:center;height:100vh;margin:0}div{text-align:center}" +
        "h1{color:#2DD4BF;font-size:28px}p{color:#9AA0A6;font-size:16px}</style>";

    private const string SuccessPage =
        "<!doctype html><html dir='rtl' lang='he'><head><meta charset='utf-8'><title>Catego</title>" + PageStyle +
        "</head><body><div><h1>התחברת לקיק בהצלחה ✓</h1><p>אפשר לסגור את הלשונית ולחזור ל-Catego.</p></div></body></html>";

    private static string ErrorPage(IReadOnlyDictionary<string, string> q)
    {
        var err = WebUtility.HtmlEncode(q.TryGetValue("error_description", out var d) ? d : q.TryGetValue("error", out var e) ? e : "לא התקבל קוד");
        return "<!doctype html><html dir='rtl' lang='he'><head><meta charset='utf-8'><title>Catego</title>" + PageStyle +
               $"</head><body><div><h1 style='color:#F2545B'>ההתחברות לא הושלמה</h1><p>{err}</p></div></body></html>";
    }

    private void Stop()
    {
        foreach (var l in _listeners)
        {
            try { l.Stop(); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        Stop();
        _listeners.Clear();
    }
}
