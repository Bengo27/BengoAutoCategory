using System.Net;
using System.Net.Sockets;
using System.Text;
using AutoCategory.Core.Models;
using AutoCategory.Core.Services;

namespace AutoCategory.Core.Widgets;

public sealed record WidgetRequest(string Method, string Path, IReadOnlyDictionary<string, string> Query)
{
    public string? Param(string name) => Query.TryGetValue(name, out var v) ? v : null;
}

public sealed record WidgetResponse(int Status, string ContentType, byte[] Body)
{
    public static WidgetResponse Html(string html) => new(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    public static WidgetResponse Json(string json) => new(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
    public static WidgetResponse Bytes(string contentType, byte[] data) => new(200, contentType, data);
    public static readonly WidgetResponse NoContent = new(204, "text/plain", Array.Empty<byte>());
    public static readonly WidgetResponse NotFound = new(404, "text/plain", Encoding.UTF8.GetBytes("not found"));
}

/// <summary>
/// שרת HTTP קטן על המחשב בלבד (127.0.0.1) שמגיש את הווידג'טים ל-OBS (Browser Source).
/// בכוונה בלי HttpListener – כדי שלא יידרשו הרשאות מנהל. כל ווידג'ט רושם את הנתיבים שלו.
/// </summary>
public sealed class WidgetServer : IDisposable
{
    public const int DefaultPort = 47700;

    private readonly ILoggingService _log;
    private readonly Dictionary<(string Method, string Path), Func<WidgetRequest, WidgetResponse>> _routes = new();
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;

    public int Port { get; }
    public bool IsRunning => _listener is not null;
    public string BaseUrl => $"http://localhost:{Port}";

    public WidgetServer(ILoggingService log, int port = DefaultPort)
    {
        _log = log;
        Port = port;
    }

    public void Map(string method, string path, Func<WidgetRequest, WidgetResponse> handler)
    {
        lock (_routes) _routes[(method.ToUpperInvariant(), path.TrimEnd('/'))] = handler;
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
            _log.Log(LogLevel.Warning, $"לא ניתן להפעיל את הווידג'טים ל-OBS (פורט {Port} תפוס – אולי התוכנה כבר פתוחה?)", ex.Message);
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
            var head = await ReadHeadAsync(stream, ct).ConfigureAwait(false);
            var lines = head.Split("\r\n");
            var first = lines[0].Split(' ');
            if (first.Length < 2) return;
            var method = first[0].ToUpperInvariant();
            var target = first[1];

            // הגנה מ-DNS rebinding: רק בקשות שמיועדות למחשב הזה
            var host = lines.Skip(1).Select(h => h.Split(':', 2)).Where(h => h.Length == 2 && h[0].Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))
                .Select(h => h[1].Trim()).FirstOrDefault() ?? "";
            if (!IsLocalHost(host))
            {
                await WriteAsync(stream, new WidgetResponse(403, "text/plain", Encoding.UTF8.GetBytes("forbidden")), ct).ConfigureAwait(false);
                return;
            }

            var path = target;
            var query = new Dictionary<string, string>(StringComparer.Ordinal);
            var q = target.IndexOf('?');
            if (q >= 0)
            {
                path = target[..q];
                foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=', 2);
                    query[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
                }
            }
            path = path.TrimEnd('/');
            if (path.Length == 0) path = "/";

            Func<WidgetRequest, WidgetResponse>? handler;
            lock (_routes) _routes.TryGetValue((method, path), out handler);
            var response = handler?.Invoke(new WidgetRequest(method, path, query)) ?? WidgetResponse.NotFound;
            await WriteAsync(stream, response, ct).ConfigureAwait(false);
        }
        catch
        {
            // חיבור שנסגר באמצע – לא חשוב
        }
    }

    private bool IsLocalHost(string host) =>
        host.Equals($"localhost:{Port}", StringComparison.OrdinalIgnoreCase) ||
        host.Equals($"127.0.0.1:{Port}", StringComparison.Ordinal) ||
        host.Equals($"[::1]:{Port}", StringComparison.Ordinal);

    private static async Task<string> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
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
        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static async Task WriteAsync(NetworkStream stream, WidgetResponse r, CancellationToken ct)
    {
        var reason = r.Status switch { 200 => "OK", 204 => "No Content", 403 => "Forbidden", 404 => "Not Found", _ => "Error" };
        var header = $"HTTP/1.1 {r.Status} {reason}\r\nContent-Type: {r.ContentType}\r\nContent-Length: {r.Body.Length}\r\n" +
                     "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct).ConfigureAwait(false);
        if (r.Body.Length > 0) await stream.WriteAsync(r.Body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }
}
