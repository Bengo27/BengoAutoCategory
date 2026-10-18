using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AutoCategory.App.Platform;
using AutoCategory.Core.Models;
using AutoCategory.Core.Services;

namespace AutoCategory.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppServices? _services;
    private TrayIconService? _tray;
    private MainWindow? _window;
    private bool _exiting;

    public static AppServices Services => ((App)Current)._services!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // מופע אחד בלבד
        _singleInstance = new Mutex(true, @"Local\BengoAutoCategory.SingleInstance", out var isFirst);
        // אחרי עדכון: הגרסה הקודמת עוד נסגרת – מחכים לה
        if (!isFirst && e.Args.Any(a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase)))
        {
            try { isFirst = _singleInstance.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { isFirst = true; }
        }
        if (!isFirst)
        {
            DialogService.ShowInfo("Bengo AutoCategory כבר פועל – חפש את האייקון במגש המערכת ליד השעון.");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        UpdateService.CleanupOldVersion();

        try
        {
            _services = new AppServices();
        }
        catch (Exception ex)
        {
            DialogService.ShowError("לא ניתן להפעיל את התוכנה:\n" + ex.Message);
            Shutdown();
            return;
        }

        var settings = _services.Settings.Current;
        TryApplyStartup(settings.StartWithWindows);

        _tray = new TrayIconService();
        _tray.OpenRequested += () => Dispatcher.InvokeAsync(ShowMainWindow);
        _tray.ToggleAutoRequested += () => Dispatcher.InvokeAsync(() =>
            _services.Settings.Update(s => s.AutoCategoryEnabled = !s.AutoCategoryEnabled));
        _tray.TogglePauseRequested += () => Dispatcher.InvokeAsync(() =>
            _services.Engine.SetPaused(!_services.Engine.IsPaused));
        _tray.ExitRequested += () => Dispatcher.InvokeAsync(ExitApplication);
        _tray.SetState(settings.AutoCategoryEnabled, false);

        _services.Settings.Changed += s => Dispatcher.InvokeAsync(() => _tray?.SetState(s.AutoCategoryEnabled, _services.Engine.IsPaused));
        _services.Engine.StateChanged += st => Dispatcher.InvokeAsync(() => _tray?.SetState(st.AutoEnabled, st.Phase == EnginePhase.Paused));
        _services.Engine.Notify += (title, text) => Dispatcher.InvokeAsync(() =>
        {
            if (_services.Settings.Current.ShowNotifications) _tray?.ShowNotification(title, text);
        });

        _services.Coordinator.GameAutoAdded += (m, lib) => Dispatcher.InvokeAsync(() =>
        {
            if (_services.Settings.Current.ShowNotifications)
                _tray?.ShowNotification($"זוהה משחק חדש: {m.Category}", $"נוסף כלל אוטומטית ({lib}). אפשר לשנות את השם בכללי קטגוריות.");
        });

        _services.Updates.UpdateAvailable += u => Dispatcher.InvokeAsync(() =>
        {
            if (_services.Settings.Current.ShowNotifications)
                _tray?.ShowNotification($"גרסה חדשה זמינה: {u.Tag}", "פתח את לוח הבקרה ולחץ \"עדכן עכשיו\".");
        });

        _window = new MainWindow(_services);
        _window.Closing += (_, args) =>
        {
            // סגירה בחלון = מזעור למגש (אם מופעל), כדי שהזיהוי ימשיך
            if (_exiting) return;
            if (_services.Settings.Current.MinimizeToTray)
            {
                args.Cancel = true;
                _window.Hide();
                if (!_trayHintShown && _services.Settings.Current.ShowNotifications)
                {
                    _trayHintShown = true;
                    _tray?.ShowNotification("Bengo AutoCategory ממשיך לפעול", "הזיהוי ממשיך ברקע. לחיצה כפולה על האייקון פותחת את לוח הבקרה.");
                }
            }
            else
            {
                args.Cancel = true;
                ExitApplication();
            }
        };
        MainWindow = _window;

        _services.Start();

        var startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (!startMinimized) ShowMainWindow();
    }

    private bool _trayHintShown;

    private void ShowMainWindow()
    {
        if (_window is null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>נקרא אחרי שהעדכון הותקן והגרסה החדשה הופעלה.</summary>
    public void ExitForUpdate() => ExitApplication();

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        _services?.Log.Log(LogLevel.Info, "התוכנה נסגרת");
        _services?.Dispose();
        _tray?.Dispose();
        _tray = null;
        _window?.Close();
        Shutdown();
    }

    public static void TryApplyStartup(bool enabled)
    {
        try { StartupRegistration.Apply(enabled); }
        catch (Exception ex) { Services.Log.Log(LogLevel.Warning, "לא ניתן לעדכן הפעלה עם Windows", ex.Message); }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.Log.Log(LogLevel.Warning, "שגיאה לא צפויה", e.Exception.ToString());
        DialogService.ShowError("אירעה שגיאה לא צפויה:\n" + e.Exception.Message);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _services?.Dispose();
        try { _singleInstance?.ReleaseMutex(); } catch { /* not owned */ }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
