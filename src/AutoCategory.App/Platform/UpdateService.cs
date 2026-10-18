using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using AutoCategory.Core.Models;
using AutoCategory.Core.Services;

namespace AutoCategory.App.Platform;

public sealed record UpdateInfo(string Tag, string Name, string Notes, string DownloadUrl, long Size, string PageUrl);

/// <summary>
/// עדכון אוטומטי מ-GitHub Releases: בודק אם יש גרסה חדשה, מוריד את ה-exe, מחליף את עצמו ומפעיל מחדש.
/// (ב-Windows אפשר לשנות שם לקובץ exe שרץ – כך שאין צורך בתוכנת עדכון נפרדת.)
/// </summary>
public sealed class UpdateService
{
    private readonly HttpClient _http;
    private readonly AppConfig _config;
    private readonly ILoggingService _log;

    public UpdateInfo? Available { get; private set; }
    public event Action<UpdateInfo>? UpdateAvailable;

    public Version CurrentVersion { get; } =
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public bool Enabled => _config.HasUpdates;

    public UpdateService(HttpClient http, AppConfig config, ILoggingService log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    /// <summary>מוחק את הגרסה הישנה שנשארה מעדכון קודם.</summary>
    public static void CleanupOldVersion()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var old = OldPath(exe);
            for (var i = 0; i < 5 && File.Exists(old); i++)
            {
                try { File.Delete(old); }
                catch { Thread.Sleep(500); }
            }
        }
        catch { /* לא קריטי */ }
    }

    public void StartPeriodicChecks()
    {
        if (!Enabled) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            while (true)
            {
                await CheckAsync(quiet: true).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromHours(6)).ConfigureAwait(false);
            }
        });
    }

    /// <summary>בודק ב-GitHub. מחזיר את העדכון אם יש גרסה חדשה יותר.</summary>
    public async Task<UpdateInfo?> CheckAsync(bool quiet = false)
    {
        if (!Enabled) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_config.GitHubRepo}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await _http.SendAsync(req).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                if (!quiet) _log.Log(LogLevel.Warning, "בדיקת עדכונים נכשלה", $"GitHub {(int)res.StatusCode}");
                return null;
            }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!UpdateVersion.IsNewer(tag, CurrentVersion))
            {
                if (!quiet) _log.Log(LogLevel.Info, $"אין עדכון חדש (הגרסה שלך: {CurrentVersion.ToString(3)})");
                return null;
            }

            string? url = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    url = a.GetProperty("browser_download_url").GetString();
                    size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                    break;
                }
            }
            if (url is null) return null;

            var info = new UpdateInfo(tag,
                root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag,
                root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                url, size,
                root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "");

            var isNew = Available?.Tag != info.Tag;
            Available = info;
            if (isNew)
            {
                _log.Log(LogLevel.Info, $"גרסה חדשה זמינה: {tag}", info.Notes);
                UpdateAvailable?.Invoke(info);
            }
            return info;
        }
        catch (Exception ex)
        {
            if (!quiet) _log.Log(LogLevel.Warning, "בדיקת עדכונים נכשלה", ex.Message);
            return null;
        }
    }

    /// <summary>מוריד, מחליף את ה-exe ומפעיל את הגרסה החדשה. אחרי שזה מצליח – התוכנה צריכה להיסגר.</summary>
    public async Task ApplyAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("לא ידוע איפה התוכנה מותקנת.");
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("עדכון אוטומטי עובד רק בגרסה המהודרת (Catego.exe).");

        var dir = Path.GetDirectoryName(exe)!;
        var tmp = Path.Combine(dir, "Catego.new.exe");

        _log.Log(LogLevel.Info, $"מוריד את גרסה {info.Tag}…");
        using (var res = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            res.EnsureSuccessStatusCode();
            var total = res.Content.Headers.ContentLength ?? info.Size;
            await using var src = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(tmp);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
            }
        }

        var length = new FileInfo(tmp).Length;
        if (length < 1_000_000 || (info.Size > 0 && length != info.Size))
        {
            File.Delete(tmp);
            throw new InvalidOperationException("הקובץ שהורד לא שלם. נסה שוב.");
        }

        // מחליפים: הנוכחי → .old, החדש → במקום הנוכחי
        var old = OldPath(exe);
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try
        {
            File.Move(tmp, exe);
        }
        catch
        {
            File.Move(old, exe); // מחזירים את המצב הקודם
            throw;
        }

        _log.Log(LogLevel.Info, $"העדכון לגרסה {info.Tag} הותקן – מפעיל מחדש");
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true, WorkingDirectory = dir });
    }

    private static string OldPath(string exe) =>
        Path.Combine(Path.GetDirectoryName(exe)!, Path.GetFileNameWithoutExtension(exe) + ".old.exe");
}
