using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AutoCategory.App;
using AutoCategory.App.Platform;
using AutoCategory.Core.Models;
using Catego.Studio.Platform;

namespace Catego.Studio;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private StudioServices? _services;
    private StudioTray? _tray;
    private MainWindow? _window;
    private bool _exiting;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\CategoStudio.SingleInstance", out var isFirst);
        if (!isFirst && e.Args.Any(a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase)))
        {
            try { isFirst = _singleInstance.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { isFirst = true; }
        }
        if (!isFirst)
        {
            DialogService.ShowInfo("Catego Studio כבר פועל – חפש את האייקון במגש המערכת ליד השעון.");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        UpdateService.CleanupOldVersion();
        StartupRegistration.ValueName = "CategoStudio";

        try { _services = new StudioServices(); }
        catch (Exception ex)
        {
            DialogService.ShowError("לא ניתן להפעיל את התוכנה:\n" + ex.Message);
            Shutdown();
            return;
        }
        var services = _services;
        TryApplyStartup(services.Settings.StartWithWindows);

        _tray = new StudioTray();
        _tray.OpenRequested += () => Dispatcher.InvokeAsync(ShowMainWindow);
        _tray.ToggleTtsRequested += () => Dispatcher.InvokeAsync(async () =>
            await services.Tts.SetEnabledAsync(!services.Tts.Settings.Current.Enabled));
        _tray.SkipRequested += () => services.Tts.SkipCurrent();
        _tray.ExitRequested += () => Dispatcher.InvokeAsync(ExitApplication);
        _tray.SetTtsState(services.Tts.Settings.Current.Enabled);
        services.Tts.Settings.Changed += s => Dispatcher.InvokeAsync(() => _tray?.SetTtsState(s.Enabled));

        services.Updates.UpdateAvailable += u => Dispatcher.InvokeAsync(() =>
        {
            if (services.Settings.ShowNotifications)
                _tray?.ShowNotification($"גרסה חדשה זמינה: {u.Tag}", "פתח את Catego Studio ולחץ \"עדכן עכשיו\".");
        });

        _window = new MainWindow(services);
        _window.Closing += (_, args) =>
        {
            if (_exiting) return;
            args.Cancel = true;
            if (services.Settings.MinimizeToTray)
            {
                _window.Hide();
                if (!_trayHintShown && services.Settings.ShowNotifications)
                {
                    _trayHintShown = true;
                    _tray?.ShowNotification("Catego Studio ממשיך לפעול", "ההקראה והווידג'טים ממשיכים ברקע. לחיצה כפולה על האייקון פותחת את התוכנה.");
                }
            }
            else ExitApplication();
        };
        MainWindow = _window;

        services.Start();
        if (!e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))) ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_window is null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

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

    public void TryApplyStartup(bool enabled)
    {
        try { StartupRegistration.Apply(enabled); }
        catch (Exception ex) { _services?.Log.Log(LogLevel.Warning, "לא ניתן לעדכן הפעלה עם Windows", ex.Message); }
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
        try { _singleInstance?.ReleaseMutex(); } catch { }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
