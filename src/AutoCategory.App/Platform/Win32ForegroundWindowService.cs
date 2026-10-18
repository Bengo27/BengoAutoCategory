using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AutoCategory.Core.Models;
using AutoCategory.Core.Services;

namespace AutoCategory.App.Platform;

/// <summary>
/// זיהוי החלון הפעיל בעזרת Windows API (בלי צילומי מסך, בלי הרשאות מנהל,
/// ובלי לגעת בזיכרון של המשחק – בטוח מול מערכות אנטי-צ'יט).
/// </summary>
public sealed class Win32ForegroundWindowService : IForegroundWindowService
{
    private readonly int _ownPid = Environment.ProcessId;

    private IReadOnlyCollection<string> _runningCache = Array.Empty<string>();
    private DateTime _runningCacheAt = DateTime.MinValue;
    private static readonly TimeSpan RunningCacheTtl = TimeSpan.FromSeconds(4);

    public ForegroundInfo GetForeground()
    {
        var now = DateTime.Now;
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return ForegroundInfo.None(now);

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return ForegroundInfo.None(now);

        var title = GetTitle(hwnd);

        // החלון של התוכנה עצמה
        if (pid == _ownPid)
            return new ForegroundInfo("BengoAutoCategory", "BengoAutoCategory.exe", title, (int)pid, now);

        string processName = "";
        try
        {
            using var p = Process.GetProcessById((int)pid);
            processName = p.ProcessName;
        }
        catch
        {
            // התהליך נסגר בדיוק עכשיו
        }

        var fullPath = GetExecutablePath((int)pid);
        var exe = string.IsNullOrEmpty(fullPath) ? "" : Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(exe) && !string.IsNullOrEmpty(processName)) exe = processName + ".exe";
        if (string.IsNullOrEmpty(exe)) return ForegroundInfo.None(now);
        if (string.IsNullOrEmpty(processName)) processName = Path.GetFileNameWithoutExtension(exe);

        return new ForegroundInfo(processName, exe, title, (int)pid, now) { ExecutablePath = string.IsNullOrEmpty(fullPath) ? null : fullPath };
    }

    public IReadOnlyCollection<string> GetRunningExecutables()
    {
        var now = DateTime.Now;
        if (now - _runningCacheAt < RunningCacheTtl) return _runningCache;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id != _ownPid && p.Id != 0) set.Add(p.ProcessName + ".exe");
            }
            catch { /* ignore */ }
            finally { p.Dispose(); }
        }

        _runningCache = set;
        _runningCacheAt = now;
        return set;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var len = GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>שם קובץ ההרצה האמיתי (למשל "League of Legends.exe"), גם כשהשם שונה משם התהליך.</summary>
    private static string GetExecutablePath(int pid)
    {
        var h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
        }
        finally
        {
            CloseHandle(h);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
