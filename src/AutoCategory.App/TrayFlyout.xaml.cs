using System.Windows;

namespace AutoCategory.App;

/// <summary>
/// חלון קטן שנפתח בלחיצה על האייקון ליד השעון (כמו חלון העוצמה של Windows).
/// מציג את הקטגוריה, מה רץ עכשיו והספירה – ונסגר לבד כשלוחצים מחוץ לו.
/// </summary>
public partial class TrayFlyout : Window
{
    private readonly AppServices _services;
    private DateTime _hiddenAt = DateTime.MinValue;

    public event Action? OpenFullRequested;

    public TrayFlyout(AppServices services, object dashboard)
    {
        InitializeComponent();
        _services = services;
        DataContext = dashboard;
        Deactivated += (_, _) => HideFlyout();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) HideFlyout(); };
        services.Engine.StateChanged += _ => Dispatcher.InvokeAsync(UpdatePauseButton);
        UpdatePauseButton();
    }

    /// <summary>לחיצה על האייקון: פותח, או סוגר אם כבר פתוח.</summary>
    public void Toggle()
    {
        // הלחיצה על האייקון כבר "הוציאה" את הפוקוס וסגרה את החלון – לא לפתוח אותו מחדש מיד
        if (IsVisible || (DateTime.Now - _hiddenAt).TotalMilliseconds < 300) { HideFlyout(); return; }
        Show();
        UpdateLayout();
        PlaceNearTray();
        Activate();
    }

    public void HideFlyout()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.Now;
        Hide();
    }

    /// <summary>בפינה ליד השעון, מעל שורת המשימות (גם אם היא למעלה או בצד).</summary>
    private void PlaceNearTray()
    {
        var area = SystemParameters.WorkArea;
        var screenW = SystemParameters.PrimaryScreenWidth;
        var h = ActualHeight > 0 ? ActualHeight : 420;
        Left = area.Right - Width - 4;
        Top = area.Bottom - h - 4;
        if (area.Top > 0) Top = area.Top + 4;            // שורת משימות למעלה
        if (area.Left > 0 && area.Right >= screenW) Left = area.Left + 4; // שורת משימות משמאל
        if (Top < area.Top) Top = area.Top;
    }

    private void UpdatePauseButton()
    {
        PauseButton.Content = _services.Engine.IsPaused ? "▶ המשך זיהוי" : "⏸ השהה זיהוי";
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _services.Engine.SetPaused(!_services.Engine.IsPaused);
        UpdatePauseButton();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        OpenFullRequested?.Invoke();
    }
}
