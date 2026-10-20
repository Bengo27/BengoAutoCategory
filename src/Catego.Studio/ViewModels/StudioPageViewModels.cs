using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using AutoCategory.App.Platform;
using AutoCategory.App.ViewModels;
using AutoCategory.Core.Models;
using AutoCategory.Core.Platforms.Kick;

namespace Catego.Studio.ViewModels;

/// <summary>עמוד "חיבור לקיק" של Studio (התחברות בלחיצה דרך השרת של Catego).</summary>
public sealed class KickConnectViewModel : ObservableObject, IPageViewModel
{
    private readonly StudioServices _s;
    private CancellationTokenSource? _connectCts;

    public ICommand ConnectCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand OpenChannelCommand { get; }

    public KickConnectViewModel(StudioServices services)
    {
        _s = services;
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && _s.Kick.HasCredentials);
        CancelCommand = new RelayCommand(() => _connectCts?.Cancel());
        DisconnectCommand = new AsyncRelayCommand(async () =>
        {
            await _s.Kick.DisconnectAsync();
            Message = "התנתקת מקיק.";
            Refresh();
        });
        OpenChannelCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(_s.Kick.ChannelSlug)) StudioServices.OpenInBrowser("https://kick.com/" + _s.Kick.ChannelSlug);
        });
        _s.Kick.ConnectionChanged += () => Application.Current?.Dispatcher.InvokeAsync(Refresh);
        Refresh();
    }

    public void OnNavigatedTo() => Refresh();

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); OnPropertyChanged(nameof(ShowConnect)); } }

    public bool IsConnected => _s.Kick.IsConnected;
    public bool ShowConnect => !IsConnected && !IsBusy;

    private string _statusTitle = "";
    public string StatusTitle { get => _statusTitle; private set => SetProperty(ref _statusTitle, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private string _message = "";
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public string ChannelText => _s.Kick.ChannelSlug is { Length: > 0 } slug ? "kick.com/" + slug : "";

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ShowConnect));
        OnPropertyChanged(nameof(ChannelText));
        if (!_s.Kick.HasCredentials)
        {
            StatusTitle = "ההתחברות לקיק לא מוגדרת בגרסה הזו";
            StatusText = "חסרים פרטי שרת ההתחברות בהגדרות ההפצה.";
        }
        else if (!IsConnected)
        {
            StatusTitle = "לא מחובר לקיק";
            StatusText = "לחץ \"התחבר לקיק\" – ייפתח דף של קיק בדפדפן, מאשרים, וחוזרים לכאן. הסיסמה לא עוברת דרך התוכנה.";
        }
        else if (!_s.Kick.HasRewardsScope)
        {
            StatusTitle = $"מחובר בתור {_s.Kick.AccountName} – אבל בלי הרשאת פרסים";
            StatusText = "התנתק והתחבר שוב, ואשר בדף של קיק את ההרשאה לפרסי נקודות.";
        }
        else
        {
            StatusTitle = $"מחובר לקיק בתור {_s.Kick.AccountName}";
            StatusText = "הרשאות: קריאת פרטי המשתמש והערוץ, וניהול פרסי נקודות (בשביל ההקראה). בלי צ'אט ובלי מפתח שידור.";
        }
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task ConnectAsync()
    {
        _connectCts = new CancellationTokenSource();
        IsBusy = true;
        Message = "נפתח דף ההתחברות של קיק בדפדפן – אשר שם וחזור לכאן…";
        try
        {
            var name = await _s.Kick.ConnectAsync(_connectCts.Token);
            Message = $"✓ התחברת בתור {name}";
        }
        catch (OperationCanceledException)
        {
            Message = "ההתחברות בוטלה.";
        }
        catch (KickException ex)
        {
            Message = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            Message = "אין חיבור לאינטרנט / לקיק: " + ex.Message;
        }
        catch (System.Net.Sockets.SocketException)
        {
            Message = "הפורט של ההתחברות תפוס – אולי Catego באמצע התחברות לקיק? סגור אותה ונסה שוב.";
        }
        finally
        {
            IsBusy = false;
            _connectCts.Dispose();
            _connectCts = null;
            Refresh();
        }
    }
}

/// <summary>יומן פעילות.</summary>
public sealed class LogViewModel : ObservableObject, IPageViewModel
{
    private readonly StudioServices _s;
    public ObservableCollection<ActivityLogEntry> Entries { get; } = new();
    public ICommand ClearCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public LogViewModel(StudioServices services)
    {
        _s = services;
        ClearCommand = new RelayCommand(() => { _s.Log.Clear(); Entries.Clear(); });
        OpenFolderCommand = new RelayCommand(() =>
        {
            try { Process.Start(new ProcessStartInfo(_s.Log.LogDirectory) { UseShellExecute = true }); } catch { }
        });
        _s.Log.EntryAdded += e => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Entries.Insert(0, e);
            while (Entries.Count > 500) Entries.RemoveAt(Entries.Count - 1);
        });
        OnNavigatedTo();
    }

    public void OnNavigatedTo()
    {
        Entries.Clear();
        foreach (var e in _s.Log.Snapshot().Reverse()) Entries.Add(e);
    }
}

/// <summary>הגדרות כלליות + עדכונים.</summary>
public sealed class StudioSettingsViewModel : ObservableObject, IPageViewModel
{
    private readonly StudioServices _s;
    public ICommand CheckUpdatesCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

    public StudioSettingsViewModel(StudioServices services)
    {
        _s = services;
        CheckUpdatesCommand = new AsyncRelayCommand(async () =>
        {
            UpdateText = "בודק…";
            var u = await _s.Updates.CheckAsync();
            UpdateText = u is null ? "אין גרסה חדשה – יש לך את האחרונה ✓" : $"גרסה חדשה זמינה: {u.Tag} – לחץ \"עדכן עכשיו\" בפס למעלה";
        }, () => _s.Updates.Enabled);
        OpenDataFolderCommand = new RelayCommand(() =>
        {
            try { Process.Start(new ProcessStartInfo(_s.DataDirectory) { UseShellExecute = true }); } catch { }
        });
        OnNavigatedTo();
    }

    public void OnNavigatedTo()
    {
        var s = _s.Settings;
        _startWithWindows = s.StartWithWindows;
        _minimizeToTray = s.MinimizeToTray;
        _showNotifications = s.ShowNotifications;
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(MinimizeToTray));
        OnPropertyChanged(nameof(ShowNotifications));
    }

    public string VersionText => $"גרסה {_s.Updates.CurrentVersion.ToString(3)}" +
                                 (_s.Updates.Enabled ? "" : " · עדכונים אוטומטיים לא הוגדרו בגרסה הזו");

    private string _updateText = "";
    public string UpdateText { get => _updateText; private set => SetProperty(ref _updateText, value); }

    private bool _startWithWindows, _minimizeToTray, _showNotifications;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetProperty(ref _startWithWindows, value)) return;
            Save();
            ((App)Application.Current).TryApplyStartup(value);
        }
    }

    public bool MinimizeToTray { get => _minimizeToTray; set { if (SetProperty(ref _minimizeToTray, value)) Save(); } }
    public bool ShowNotifications { get => _showNotifications; set { if (SetProperty(ref _showNotifications, value)) Save(); } }

    private void Save() => _s.SaveSettings(new StudioSettings
    {
        StartWithWindows = _startWithWindows,
        MinimizeToTray = _minimizeToTray,
        ShowNotifications = _showNotifications,
    });
}

/// <summary>הפס העליון "גרסה חדשה זמינה".</summary>
public sealed class UpdateBannerViewModel : ObservableObject
{
    private readonly StudioServices _s;
    public ICommand UpdateCommand { get; }

    public UpdateBannerViewModel(StudioServices services)
    {
        _s = services;
        UpdateCommand = new AsyncRelayCommand(ApplyAsync, () => _s.Updates.Available is not null && !_busy);
        _s.Updates.UpdateAvailable += u => Application.Current?.Dispatcher.InvokeAsync(() => Show(u));
        if (_s.Updates.Available is { } a) Show(a);
    }

    private bool _visible;
    public bool Visible { get => _visible; private set => SetProperty(ref _visible, value); }

    private string _text = "";
    public string Text { get => _text; private set => SetProperty(ref _text, value); }

    private bool _busy;

    private void Show(UpdateInfo u)
    {
        Visible = true;
        Text = $"גרסה חדשה זמינה: {u.Tag}" + (string.IsNullOrWhiteSpace(u.Notes) ? "" : " · " + u.Notes.Split('\n')[0].Trim());
    }

    private async Task ApplyAsync()
    {
        if (_s.Updates.Available is not { } info) return;
        _busy = true;
        try
        {
            var progress = new Progress<double>(p => Text = $"מוריד… {p:P0}");
            await _s.Updates.ApplyAsync(info, progress);
            ((App)Application.Current).ExitForUpdate();
        }
        catch (Exception ex)
        {
            Text = "העדכון נכשל: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }
}
