using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AutoCategory.Core.Models;
using AutoCategory.Core.Services;

namespace AutoCategory.App.ViewModels;

public interface IPageViewModel
{
    void OnNavigatedTo();
}

public sealed class DashboardViewModel : ObservableObject, IPageViewModel
{
    private readonly AppServices _s;
    private DetectionResult? _lastDetection;

    public ObservableCollection<ActivityLogEntry> RecentLog { get; } = new();

    public ICommand CancelCommand { get; }
    public ICommand ChangeNowCommand { get; }
    public ICommand AddMappingCommand { get; }
    public ICommand IgnoreCurrentCommand { get; }
    public ICommand AddSiteCommand { get; }
    public ICommand SetCurrentCategoryCommand { get; }
    public ICommand OpenTestModeCommand { get; }
    public ICommand UpdateCommand { get; }

    public DashboardViewModel(AppServices services, Action openTestMode)
    {
        _s = services;
        _autoEnabled = services.Settings.Current.AutoCategoryEnabled;
        _currentCategory = services.Engine.CurrentCategory;

        CancelCommand = new RelayCommand(() => _s.Engine.Cancel(DateTime.Now), () => CanCancel);
        ChangeNowCommand = new AsyncRelayCommand(() => _s.Engine.ChangeNowAsync(DateTime.Now), () => CanChangeNow);
        AddMappingCommand = new RelayCommand(AddMapping, () => ShowAddMapping);
        IgnoreCurrentCommand = new RelayCommand(IgnoreCurrent, () => ShowAddMapping);
        AddSiteCommand = new RelayCommand(AddSite, () => ShowAddSite);
        SetCurrentCategoryCommand = new RelayCommand(SetCurrentCategory);
        OpenTestModeCommand = new RelayCommand(openTestMode);
        UpdateCommand = new AsyncRelayCommand(ApplyUpdateAsync, () => _s.Updates.Available is not null && !IsUpdating);

        _s.Updates.UpdateAvailable += u => Ui(() => ShowUpdate(u));
        if (_s.Updates.Available is { } avail) ShowUpdate(avail);

        foreach (var e in _s.Log.Snapshot().TakeLast(RecentMax).Reverse()) RecentLog.Add(e);

        _s.Coordinator.DetectionUpdated += d => Ui(() => ApplyDetection(d));
        _s.Engine.StateChanged += st => Ui(() => ApplyState(st));
        _s.Settings.Changed += st => Ui(() => { AutoEnabled = st.AutoCategoryEnabled; OnPropertyChanged(nameof(IsTestMode)); });
        _s.Log.EntryAdded += e => Ui(() =>
        {
            RecentLog.Insert(0, e);
            while (RecentLog.Count > RecentMax) RecentLog.RemoveAt(RecentLog.Count - 1);
        });

        if (_s.Coordinator.LastDisplayed is { } last) ApplyDetection(last);
        ApplyState(_s.Engine.State);
    }

    private const int RecentMax = 6;

    // ───────────── עדכון ─────────────

    private bool _updateVisible;
    public bool UpdateVisible { get => _updateVisible; private set => SetProperty(ref _updateVisible, value); }

    private string _updateText = "";
    public string UpdateText { get => _updateText; private set => SetProperty(ref _updateText, value); }

    private bool _isUpdating;
    public bool IsUpdating { get => _isUpdating; private set { if (SetProperty(ref _isUpdating, value)) CommandManager.InvalidateRequerySuggested(); } }

    private void ShowUpdate(AutoCategory.App.Platform.UpdateInfo u)
    {
        UpdateVisible = true;
        UpdateText = $"גרסה חדשה זמינה: {u.Tag} (אצלך: {_s.Updates.CurrentVersion.ToString(3)})";
    }

    private async Task ApplyUpdateAsync()
    {
        var u = _s.Updates.Available;
        if (u is null) return;
        IsUpdating = true;
        try
        {
            var progress = new Progress<double>(p => UpdateText = $"מוריד את {u.Tag}… {p:P0}");
            await _s.Updates.ApplyAsync(u, progress);
            UpdateText = "מפעיל מחדש…";
            ((App)Application.Current).ExitForUpdate();
        }
        catch (Exception ex)
        {
            UpdateText = "העדכון נכשל: " + ex.Message;
            _s.Log.Log(LogLevel.Warning, "העדכון נכשל", ex.Message);
        }
        finally
        {
            IsUpdating = false;
        }
    }

    public void OnNavigatedTo() => OnPropertyChanged(nameof(IsTestMode));

    // ───────────── מתג אוטומטי ─────────────

    private bool _autoEnabled;
    public bool AutoEnabled
    {
        get => _autoEnabled;
        set
        {
            if (!SetProperty(ref _autoEnabled, value)) return;
            if (_s.Settings.Current.AutoCategoryEnabled != value)
            {
                _s.Settings.Update(st => st.AutoCategoryEnabled = value);
                _s.Log.Log(LogLevel.Info, value ? "קטגוריה אוטומטית הופעלה" : "קטגוריה אוטומטית כובתה – התוכנה רק תציע שינויים");
            }
        }
    }

    public bool IsTestMode => _s.Coordinator.TestMode;
    public string ChangeServiceName => _s.Changer.DisplayName;

    // ───────────── תוכנה נוכחית ─────────────

    private string _appTitle = "—";
    public string AppTitle { get => _appTitle; private set => SetProperty(ref _appTitle, value); }

    private string _processName = "—";
    public string ProcessName { get => _processName; private set => SetProperty(ref _processName, value); }

    private string _executableName = "—";
    public string ExecutableName { get => _executableName; private set => SetProperty(ref _executableName, value); }

    private string _windowTitle = "—";
    public string WindowTitle { get => _windowTitle; private set => SetProperty(ref _windowTitle, value); }

    private string _detectedAt = "—";
    public string DetectedAt { get => _detectedAt; private set => SetProperty(ref _detectedAt, value); }

    private string _kindText = "ממתין…";
    public string KindText { get => _kindText; private set => SetProperty(ref _kindText, value); }

    private Brush _kindBrush = Brushes.Gray;
    public Brush KindBrush { get => _kindBrush; private set => SetProperty(ref _kindBrush, value); }

    // ───────────── קטגוריה שזוהתה ─────────────

    private string _detectedCategory = "—";
    public string DetectedCategory { get => _detectedCategory; private set => SetProperty(ref _detectedCategory, value); }

    private int _confidence;
    public int Confidence { get => _confidence; private set => SetProperty(ref _confidence, value); }

    private string _confidenceText = "0%";
    public string ConfidenceText { get => _confidenceText; private set => SetProperty(ref _confidenceText, value); }

    private string _confidenceHint = "";
    public string ConfidenceHint { get => _confidenceHint; private set => SetProperty(ref _confidenceHint, value); }

    private bool _showAddSite;
    public bool ShowAddSite { get => _showAddSite; private set => SetProperty(ref _showAddSite, value); }

    private bool _showAddMapping;
    public bool ShowAddMapping { get => _showAddMapping; private set => SetProperty(ref _showAddMapping, value); }

    // ───────────── קטגוריה נוכחית + סטטוס ─────────────

    private string _currentCategory;
    public string CurrentCategory { get => _currentCategory; private set => SetProperty(ref _currentCategory, value); }

    private string _phaseTitle = "ממתין לזיהוי";
    public string PhaseTitle { get => _phaseTitle; private set => SetProperty(ref _phaseTitle, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private Brush _phaseBrush = Brushes.Gray;
    public Brush PhaseBrush { get => _phaseBrush; private set => SetProperty(ref _phaseBrush, value); }

    private bool _showCountdown;
    public bool ShowCountdown { get => _showCountdown; private set => SetProperty(ref _showCountdown, value); }

    private string _countdownText = "";
    public string CountdownText { get => _countdownText; private set => SetProperty(ref _countdownText, value); }

    private string _countdownLabel = "";
    public string CountdownLabel { get => _countdownLabel; private set => SetProperty(ref _countdownLabel, value); }

    private double _countdownProgress;
    public double CountdownProgress { get => _countdownProgress; private set => SetProperty(ref _countdownProgress, value); }

    private bool _canCancel;
    public bool CanCancel { get => _canCancel; private set => SetProperty(ref _canCancel, value); }

    private bool _canChangeNow;
    public bool CanChangeNow { get => _canChangeNow; private set => SetProperty(ref _canChangeNow, value); }

    private string _changeNowText = "שנה עכשיו";
    public string ChangeNowText { get => _changeNowText; private set => SetProperty(ref _changeNowText, value); }

    // ───────────── עדכונים ─────────────

    private void ApplyDetection(DetectionResult d)
    {
        _lastDetection = d;
        var fg = d.Foreground;
        ProcessName = Or(fg.ProcessName);
        ExecutableName = Or(fg.ExecutableName);
        WindowTitle = Or(fg.WindowTitle);
        DetectedAt = fg.Timestamp.ToString("HH:mm:ss");

        switch (d.Kind)
        {
            case DetectionKind.Game:
                AppTitle = d.Category ?? fg.ExecutableName;
                KindText = "משחק מוכר";
                KindBrush = Res("AccentBrush");
                DetectedCategory = d.Category ?? "—";
                ConfidenceHint = d.Site is not null ? $"זוהה לפי הלשונית בדפדפן: \"{d.Site.Keyword}\""
                    : d.Confidence >= 100 ? "התאמה מדויקת לקובץ ההרצה" : "קובץ דו-משמעי – זוהה לפי כותרת החלון";
                ShowAddMapping = false;
                ShowAddSite = false;
                break;
            case DetectionKind.Ignored:
                AppTitle = fg.ExecutableName;
                KindText = "ברשימת ההתעלמות";
                KindBrush = Res("MutedBrush");
                DetectedCategory = "—";
                var isBrowser = _s.Mappings.IsBrowser(fg.ExecutableName);
                ConfidenceHint = isBrowser ? "דפדפן – הקטגוריה תשתנה רק בלשונית שיש לה כלל אתר" : "תוכנה זו לעולם לא משנה קטגוריה";
                ShowAddMapping = false;
                ShowAddSite = isBrowser;
                break;
            case DetectionKind.Unknown:
                AppTitle = "תוכנה לא מוכרת";
                KindText = "לא מוכרת";
                KindBrush = Res("WarningBrush");
                DetectedCategory = "—";
                ConfidenceHint = "אין כלל לתוכנה הזו. משחק? הוסף כלל.";
                ShowAddMapping = true;
                ShowAddSite = false;
                break;
            default:
                AppTitle = "—";
                KindText = "אין חלון פעיל";
                KindBrush = Res("MutedBrush");
                DetectedCategory = "—";
                ConfidenceHint = "";
                ShowAddMapping = false;
                ShowAddSite = false;
                break;
        }

        Confidence = d.Kind == DetectionKind.Game ? d.Confidence : 0;
        ConfidenceText = d.Kind == DetectionKind.Ignored ? "—" : $"{Confidence}%";
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyState(EngineState st)
    {
        CurrentCategory = st.CurrentCategory;
        StatusText = st.StatusText;
        OnPropertyChanged(nameof(ChangeServiceName));
        var settings = _s.Settings.Current;

        (PhaseTitle, PhaseBrush) = st.Phase switch
        {
            EnginePhase.Pending => ("מוצע שינוי קטגוריה", Res("AccentBrush")),
            EnginePhase.PendingPaused => ("הספירה מושהית", Res("WarningBrush")),
            EnginePhase.AwaitingManual => ("ממתין לאישורך", Res("InfoBrush")),
            EnginePhase.Cooldown => ("המתנה בין שינויים", Res("WarningBrush")),
            EnginePhase.ReturningToDefault => ("אין משחק פעיל", Res("InfoBrush")),
            EnginePhase.Paused => ("הזיהוי מושהה", Res("DangerBrush")),
            _ => ("הקטגוריה מעודכנת", Res("MutedBrush")),
        };

        double total = st.Phase switch
        {
            EnginePhase.Pending or EnginePhase.PendingPaused => settings.VerificationDelaySeconds,
            EnginePhase.Cooldown => settings.MinSecondsBetweenChanges,
            EnginePhase.ReturningToDefault => settings.ReturnToDefaultMinutes * 60,
            _ => 0,
        };

        ShowCountdown = total > 0 && st.SecondsRemaining > 0;
        CountdownText = st.SecondsRemaining >= 60
            ? $"{st.SecondsRemaining / 60}:{st.SecondsRemaining % 60:00}"
            : st.SecondsRemaining.ToString();
        CountdownLabel = st.Phase switch
        {
            EnginePhase.ReturningToDefault => $"עד חזרה ל-{settings.DefaultCategory}",
            EnginePhase.Cooldown => "עד שיתאפשר שינוי",
            _ => st.SecondsRemaining >= 60 ? "דקות" : "שניות עד השינוי",
        };
        CountdownProgress = total > 0 ? Math.Clamp((total - st.SecondsRemaining) / total * 100, 0, 100) : 0;

        CanCancel = st.Phase is EnginePhase.Pending or EnginePhase.PendingPaused or EnginePhase.AwaitingManual
            or EnginePhase.Cooldown or EnginePhase.ReturningToDefault;

        var target = st.PendingCategory
                     ?? (_lastDetection is { Kind: DetectionKind.Game } d && !Same(d.Category, st.CurrentCategory) ? d.Category : null)
                     ?? (st.Phase is EnginePhase.ReturningToDefault or EnginePhase.AwaitingManual or EnginePhase.Cooldown ? settings.DefaultCategory : null);
        CanChangeNow = target is not null && !Same(target, st.CurrentCategory) && st.Phase != EnginePhase.Paused;
        ChangeNowText = CanChangeNow ? $"שנה עכשיו ל-{target}" : "שנה עכשיו";

        CommandManager.InvalidateRequerySuggested();
    }

    // ───────────── פעולות ─────────────

    private void AddMapping()
    {
        var fg = _lastDetection?.Foreground;
        if (fg is null || fg.IsEmpty) return;

        var prefill = new GameMapping { Executable = fg.ExecutableName, Category = GuessCategory(fg) };
        var result = DialogService.EditMapping(prefill, "הוספת כלל חדש", _s.Mappings.KnownCategories());
        if (result is null) return;
        _s.Mappings.AddOrUpdate(result);
        _s.Log.Log(LogLevel.Info, $"נוסף כלל: {result.Executable} → {result.Category}");
    }

    private void AddSite()
    {
        var title = _lastDetection?.Foreground.WindowTitle ?? "";
        var r = DialogService.EditSite(new SiteRule { Keyword = GuessSiteKeyword(title), Category = "" },
            "כלל אתר חדש", _s.Mappings.KnownCategories());
        if (r is null) return;
        _s.Mappings.AddOrUpdateSite(r);
        _s.Log.Log(LogLevel.Info, $"נוסף כלל אתר: \"{r.Keyword}\" → {r.Category}");
    }

    /// <summary>"Stake.com | Sweet Bonanza - Google Chrome" → "Stake.com"</summary>
    private static string GuessSiteKeyword(string title)
    {
        var t = title;
        foreach (var suffix in new[] { " - Google Chrome", " - Microsoft Edge", " — Mozilla Firefox", " - Mozilla Firefox", " - Brave", " - Opera", " - Vivaldi" })
        {
            var i = t.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            if (i > 0) { t = t[..i]; break; }
        }
        var cut = t.IndexOfAny(new[] { '|', '-', '–', '—', ':' });
        if (cut > 1) t = t[..cut];
        t = t.Trim();
        return t.Length > 40 ? t[..40] : t;
    }

    private void IgnoreCurrent()
    {
        var exe = _lastDetection?.Foreground.ExecutableName;
        if (string.IsNullOrEmpty(exe)) return;
        _s.Mappings.AddIgnored(exe);
        _s.Log.Log(LogLevel.Info, $"{exe} נוסף לרשימת ההתעלמות");
    }

    private void SetCurrentCategory()
    {
        var value = DialogService.Prompt("קטגוריה נוכחית",
            "מה הקטגוריה שמוגדרת עכשיו בסטרים שלך?\n(בשלב 1 התוכנה לא מחוברת לקיק, אז אפשר לעדכן אותה ידנית.)",
            CurrentCategory);
        if (!string.IsNullOrWhiteSpace(value)) _s.Engine.SetCurrentCategoryManually(value);
    }

    /// <summary>ניחוש שם קטגוריה מכותרת החלון (המשתמש יכול לתקן).</summary>
    private static string GuessCategory(ForegroundInfo fg)
    {
        var title = fg.WindowTitle?.Trim() ?? "";
        if (title.Length is > 0 and <= 60) return title;
        return fg.ProcessName;
    }

    private static string Or(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    private static void Ui(Action a) => Application.Current?.Dispatcher.InvokeAsync(a);
}
