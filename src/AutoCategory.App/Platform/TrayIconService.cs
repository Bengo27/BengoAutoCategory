using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AutoCategory.App.Platform;

/// <summary>אייקון במגש המערכת (ליד השעון) עם תפריט בעברית.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _autoItem;
    private readonly WinForms.ToolStripMenuItem _pauseItem;

    public event Action? OpenRequested;
    public event Action? ToggleAutoRequested;
    public event Action? TogglePauseRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        var menu = new WinForms.ContextMenuStrip
        {
            RightToLeft = WinForms.RightToLeft.Yes,
            ShowImageMargin = false,
            BackColor = Drawing.Color.FromArgb(27, 30, 37),
            ForeColor = Drawing.Color.FromArgb(232, 234, 237),
            Font = new Drawing.Font("Segoe UI", 10f),
        };

        var title = new WinForms.ToolStripMenuItem("Bengo AutoCategory") { Enabled = false };
        _autoItem = new WinForms.ToolStripMenuItem("קטגוריה אוטומטית: פועל");
        var open = new WinForms.ToolStripMenuItem("פתח לוח בקרה");
        _pauseItem = new WinForms.ToolStripMenuItem("השהה זיהוי");
        var exit = new WinForms.ToolStripMenuItem("יציאה");

        _autoItem.Click += (_, _) => ToggleAutoRequested?.Invoke();
        open.Click += (_, _) => OpenRequested?.Invoke();
        _pauseItem.Click += (_, _) => TogglePauseRequested?.Invoke();
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(title);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_autoItem);
        menu.Items.Add(open);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exit);

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Bengo AutoCategory",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
    }

    public void SetState(bool autoEnabled, bool paused)
    {
        _autoItem.Text = autoEnabled ? "קטגוריה אוטומטית: פועל" : "קטגוריה אוטומטית: כבוי";
        _pauseItem.Text = paused ? "המשך זיהוי" : "השהה זיהוי";
        var text = paused ? "Bengo AutoCategory – מושהה" : "Bengo AutoCategory";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

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
        catch { /* fallback below */ }
        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
