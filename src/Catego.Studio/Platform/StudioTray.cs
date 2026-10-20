using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Catego.Studio.Platform;

/// <summary>אייקון במגש המערכת עם תפריט קצר.</summary>
public sealed class StudioTray : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _ttsItem;
    private readonly WinForms.ToolStripMenuItem _skipItem;

    public event Action? OpenRequested;
    public event Action? ToggleTtsRequested;
    public event Action? SkipRequested;
    public event Action? ExitRequested;

    public StudioTray()
    {
        var menu = new WinForms.ContextMenuStrip
        {
            RightToLeft = WinForms.RightToLeft.Yes,
            ShowImageMargin = false,
            BackColor = Drawing.Color.FromArgb(27, 30, 37),
            ForeColor = Drawing.Color.FromArgb(232, 234, 237),
            Font = new Drawing.Font("Segoe UI", 10f),
        };
        var title = new WinForms.ToolStripMenuItem("Catego Studio") { Enabled = false };
        var open = new WinForms.ToolStripMenuItem("פתח");
        _ttsItem = new WinForms.ToolStripMenuItem("הקראה: כבויה");
        _skipItem = new WinForms.ToolStripMenuItem("דלג על ההקראה");
        var exit = new WinForms.ToolStripMenuItem("יציאה");
        open.Click += (_, _) => OpenRequested?.Invoke();
        _ttsItem.Click += (_, _) => ToggleTtsRequested?.Invoke();
        _skipItem.Click += (_, _) => SkipRequested?.Invoke();
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            title, new WinForms.ToolStripSeparator(), open, _ttsItem, _skipItem, new WinForms.ToolStripSeparator(), exit,
        });

        _icon = new WinForms.NotifyIcon { Icon = LoadIcon(), Text = "Catego Studio", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
    }

    public void SetTtsState(bool enabled) => _ttsItem.Text = enabled ? "הקראה: פעילה" : "הקראה: כבויה";

    public void ShowNotification(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = string.IsNullOrWhiteSpace(text) ? " " : text;
        _icon.BalloonTipIcon = WinForms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(4000);
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info is not null)
            {
                using var stream = info.Stream;
                return new Drawing.Icon(stream);
            }
        }
        catch { }
        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
