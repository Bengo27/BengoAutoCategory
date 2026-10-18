using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AutoCategory.App.ViewModels;
using AutoCategory.App.Views;

namespace AutoCategory.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Func<FrameworkElement>> _factories;
    private readonly Dictionary<string, FrameworkElement> _pages = new();

    public DashboardViewModel Dashboard { get; }

    public MainWindow(AppServices services)
    {
        InitializeComponent();

        Dashboard = new DashboardViewModel(services, () => NavTest.IsChecked = true);

        _factories = new()
        {
            [nameof(NavDashboard)] = () => new DashboardView { DataContext = Dashboard },
            [nameof(NavRules)] = () => new CategoryRulesView { DataContext = new CategoryRulesViewModel(services) },
            [nameof(NavIgnore)] = () => new IgnoreListView { DataContext = new IgnoreListViewModel(services) },
            [nameof(NavLog)] = () => new ActivityLogView { DataContext = new ActivityLogViewModel(services) },
            [nameof(NavKick)] = () => new KickView { DataContext = new KickViewModel(services) },
            [nameof(NavTest)] = () => new TestModeView { DataContext = new TestModeViewModel(services) },
            [nameof(NavSettings)] = () => new SettingsView { DataContext = new SettingsViewModel(services) },
        };

        var v = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = $"גרסה {v?.Major}.{v?.Minor}.{v?.Build}";

        Navigate(nameof(NavDashboard));
        SourceInitialized += (_, _) => UseDarkTitleBar();

        _services = services;
        services.Kick.ConnectionChanged += () => Dispatcher.InvokeAsync(UpdateModeBadge);
        services.Settings.Changed += _ => Dispatcher.InvokeAsync(UpdateModeBadge);
        UpdateModeBadge();
    }

    private readonly AppServices _services;

    /// <summary>התג בתחתית סרגל הצד: סימולציה / מחובר לקיק.</summary>
    private void UpdateModeBadge()
    {
        var kick = _services.Kick;
        if (_services.IsLive)
        {
            SetBadge("#0F2E2B", "AccentBrush", "מחובר לקיק", $"{kick.AccountName} – שינויים אמיתיים בערוץ");
        }
        else if (kick.IsConnected)
        {
            SetBadge("#2A2410", "WarningBrush", "מחובר לקיק · סימולציה", "שינוי אמיתי כבוי – רק רישום ביומן");
        }
        else
        {
            SetBadge("#2A2410", "WarningBrush", "מצב סימולציה", "לא מחובר לקיק – לחץ כאן כדי להתחבר");
        }
    }

    private void SetBadge(string background, string accentKey, string title, string text)
    {
        var accent = (System.Windows.Media.Brush)FindResource(accentKey);
        ModeBadge.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(background)!;
        ModeBadge.BorderBrush = accent;
        ModeTitle.Foreground = accent;
        ModeTitle.Text = title;
        ModeText.Text = text;
    }

    private void ModeBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => NavKick.IsChecked = true;

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Checked נורה כבר בזמן InitializeComponent – לפני שהמילון מוכן
        if (_factories is null) return;
        if (sender is RadioButton rb) Navigate(rb.Name);
    }

    private void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = _factories[key]();
            _pages[key] = page;
        }
        if (page.DataContext is IPageViewModel vm) vm.OnNavigatedTo();
        PageHost.Content = page;
    }

    /// <summary>שורת כותרת כהה ב-Windows 10/11.</summary>
    private void UseDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int)); // גרסאות ישנות של Windows 10
        }
        catch { /* לא קריטי */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
