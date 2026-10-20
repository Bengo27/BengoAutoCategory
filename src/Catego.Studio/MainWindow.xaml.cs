using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Catego.Studio.ViewModels;
using Catego.Studio.Views;

namespace Catego.Studio;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Func<FrameworkElement>> _factories;
    private readonly Dictionary<string, FrameworkElement> _pages = new();

    public MainWindow(StudioServices services)
    {
        InitializeComponent();

        _factories = new()
        {
            [nameof(NavTts)] = () => new TtsView { DataContext = new TtsViewModel(services, () => NavKick.IsChecked = true) },
            [nameof(NavNowPlaying)] = () => new NowPlayingView(),
            [nameof(NavKick)] = () => new KickConnectView { DataContext = new KickConnectViewModel(services) },
            [nameof(NavLog)] = () => new LogView { DataContext = new LogViewModel(services) },
            [nameof(NavSettings)] = () => new StudioSettingsView { DataContext = new StudioSettingsViewModel(services) },
        };

        UpdateBanner.DataContext = new UpdateBannerViewModel(services);
        var v = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = $"גרסה {v?.Major}.{v?.Minor}.{v?.Build}";

        Navigate(nameof(NavTts));
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_factories is null) return; // נורה כבר בזמן InitializeComponent
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

    private void UseDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
