using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AutoCategory.Core.Models;
using AutoCategory.Core.Storage;

namespace AutoCategory.App.ViewModels;

// ═══════════════════════════ כללי קטגוריות ═══════════════════════════

public sealed class CategoryRulesViewModel : ObservableObject, IPageViewModel
{
    private readonly AppServices _s;
    private List<GameMapping> _all = new();

    public ObservableCollection<GameMapping> Mappings { get; } = new();
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenFileCommand { get; }

    public CategoryRulesViewModel(AppServices services)
    {
        _s = services;
        AddCommand = new RelayCommand(Add);
        EditCommand = new RelayCommand(Edit, () => Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null);
        OpenFileCommand = new RelayCommand(() => Shell.OpenFolderSelecting(_s.Mappings.FilePath));
        _s.Mappings.Changed += () => Application.Current?.Dispatcher.InvokeAsync(Reload);
        Reload();
    }

    public void OnNavigatedTo() => Reload();

    private GameMapping? _selected;
    public GameMapping? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    private string _search = "";
    public string Search
    {
        get => _search;
        set { if (SetProperty(ref _search, value)) ApplyFilter(); }
    }

    private string _countText = "";
    public string CountText { get => _countText; private set => SetProperty(ref _countText, value); }

    private void Reload()
    {
        _all = _s.Mappings.GetMappings()
            .OrderBy(m => m.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Executable, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = Search.Trim();
        Mappings.Clear();
        foreach (var m in _all.Where(m => q.Length == 0
                     || m.Executable.Contains(q, StringComparison.OrdinalIgnoreCase)
                     || m.Category.Contains(q, StringComparison.OrdinalIgnoreCase)))
            Mappings.Add(m);
        CountText = $"{Mappings.Count} כללים";
    }

    private void Add()
    {
        var result = DialogService.EditMapping(null, "הוספת כלל חדש", _s.Mappings.KnownCategories());
        if (result is null) return;
        _s.Mappings.AddOrUpdate(result);
        _s.Log.Log(LogLevel.Info, $"נוסף כלל: {result.Executable} → {result.Category}");
    }

    public void Edit()
    {
        if (Selected is null) return;
        var original = Selected;
        var result = DialogService.EditMapping(original.Clone(), "עריכת כלל", _s.Mappings.KnownCategories());
        if (result is null) return;
        _s.Mappings.Replace(original, result);
        _s.Log.Log(LogLevel.Info, $"כלל עודכן: {result.Executable} → {result.Category}");

        // שם הקטגוריה השתנה? מציעים לעדכן גם כללים אחרים עם השם הישן (למשל GTA5.exe ו-GTA5_Enhanced.exe)
        if (!string.Equals(original.Category, result.Category, StringComparison.OrdinalIgnoreCase))
        {
            var others = _s.Mappings.GetMappings()
                .Where(m => string.Equals(m.Category, original.Category, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (others.Count > 0 && DialogService.Confirm(
                    $"יש עוד {others.Count} כללים עם הקטגוריה \"{original.Category}\":\n" +
                    string.Join("\n", others.Select(o => "• " + o.Executable)) +
                    $"\n\nלעדכן גם אותם ל-\"{result.Category}\"?", "עדכון כללים נוספים"))
            {
                foreach (var o in others)
                {
                    var u = o.Clone();
                    u.Category = result.Category;
                    u.KickCategoryId = result.KickCategoryId;
                    _s.Mappings.Replace(o, u);
                }
                _s.Log.Log(LogLevel.Info, $"עודכנו עוד {others.Count} כללים ל-{result.Category}");
            }
        }

        // אם הקטגוריה הנוכחית בסטרים היא השם הישן – מעדכנים גם אותה, כדי שלא ייראה כמו שינוי
        if (string.Equals(_s.Engine.CurrentCategory, original.Category, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(original.Category, result.Category, StringComparison.OrdinalIgnoreCase))
            _s.Engine.SetCurrentCategoryManually(result.Category);
    }

    private void Delete()
    {
        if (Selected is null) return;
        var m = Selected;
        if (!DialogService.Confirm($"למחוק את הכלל\n{m.Executable} → {m.Category}?", "מחיקת כלל")) return;
        _s.Mappings.Remove(m);
        _s.Log.Log(LogLevel.Info, $"כלל נמחק: {m.Executable}");
    }
}

// ═══════════════════════════ רשימת התעלמות ═══════════════════════════

public sealed class IgnoreListViewModel : ObservableObject, IPageViewModel
{
    private readonly AppServices _s;

    public ObservableCollection<string> Items { get; } = new();
    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    public IgnoreListViewModel(AppServices services)
    {
        _s = services;
        AddCommand = new RelayCommand(Add, () => !string.IsNullOrWhiteSpace(NewItem));
        RemoveCommand = new RelayCommand(Remove, () => Selected is not null);
        _s.Mappings.Changed += () => Application.Current?.Dispatcher.InvokeAsync(Reload);
        Reload();
    }

    public void OnNavigatedTo() => Reload();

    private string? _selected;
    public string? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    private string _newItem = "";
    public string NewItem { get => _newItem; set => SetProperty(ref _newItem, value); }

    private void Reload()
    {
        var keep = Selected;
        Items.Clear();
        foreach (var i in _s.Mappings.GetIgnoreList().OrderBy(i => i, StringComparer.OrdinalIgnoreCase)) Items.Add(i);
        Selected = keep is not null && Items.Contains(keep) ? keep : null;
    }

    private void Add()
    {
        var exe = NewItem.Trim();
        if (exe.Length == 0) return;
        if (_s.Mappings.HasAnyMappingFor(exe) &&
            !DialogService.Confirm($"ל-{exe} יש כלל קטגוריה. כלל מיפוי גובר על רשימת ההתעלמות, כך שהוא עדיין יזוהה.\nלהוסיף בכל זאת?"))
            return;
        try
        {
            _s.Mappings.AddIgnored(exe);
            _s.Log.Log(LogLevel.Info, $"{exe} נוסף לרשימת ההתעלמות");
            NewItem = "";
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex.Message);
        }
    }

    private void Remove()
    {
        if (Selected is null) return;
        var exe = Selected;
        _s.Mappings.RemoveIgnored(exe);
        _s.Log.Log(LogLevel.Info, $"{exe} הוסר מרשימת ההתעלמות");
    }
}

// ═══════════════════════════ יומן פעילות ═══════════════════════════

public sealed class ActivityLogViewModel : ObservableObject, IPageViewModel
{
    private const int MaxVisible = 500;
    private readonly AppServices _s;

    public ObservableCollection<ActivityLogEntry> Entries { get; } = new();
    public ICommand ClearCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public ActivityLogViewModel(AppServices services)
    {
        _s = services;
        foreach (var e in _s.Log.Snapshot().Reverse()) Entries.Add(e);
        _s.Log.EntryAdded += e => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Entries.Insert(0, e);
            while (Entries.Count > MaxVisible) Entries.RemoveAt(Entries.Count - 1);
        });
        ClearCommand = new RelayCommand(() => { _s.Log.Clear(); Entries.Clear(); });
        OpenFolderCommand = new RelayCommand(() => Shell.OpenFolder(_s.Log.LogDirectory));
    }

    public void OnNavigatedTo() { }
}

// ═══════════════════════════ מצב בדיקה ═══════════════════════════

public sealed class TestModeViewModel : ObservableObject, IPageViewModel
{
    private readonly AppServices _s;

    public IReadOnlyList<string> Executables { get; } = DefaultData.TestExecutables;
    public ICommand SimulateCommand { get; }
    public ICommand SimulateCustomCommand { get; }
    public ICommand QuickDelayCommand { get; }

    public TestModeViewModel(AppServices services)
    {
        _s = services;
        SimulateCommand = new RelayCommand(p => Simulate(p as string));
        SimulateCustomCommand = new RelayCommand(() => Simulate(CustomExe), () => !string.IsNullOrWhiteSpace(CustomExe));
        QuickDelayCommand = new RelayCommand(() =>
        {
            _s.Settings.Update(st => { st.VerificationDelaySeconds = 5; st.MinSecondsBetweenChanges = 10; });
            _s.Log.Log(LogLevel.Info, "מצב בדיקה: זמן אימות 5 שניות, זמן בין שינויים 10 שניות");
            OnPropertyChanged(nameof(DelayText));
        });
        _currentFake = _s.Coordinator.FakeProvider.CurrentExecutable;
    }

    public void OnNavigatedTo()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(DelayText));
    }

    public bool IsEnabled
    {
        get => _s.Coordinator.TestMode;
        set
        {
            if (_s.Coordinator.TestMode == value) return;
            _s.Coordinator.TestMode = value;
            OnPropertyChanged();
        }
    }

    private string _currentFake;
    public string CurrentFake { get => _currentFake; private set => SetProperty(ref _currentFake, value); }

    private string _customExe = "";
    public string CustomExe { get => _customExe; set => SetProperty(ref _customExe, value); }

    public string DelayText
    {
        get
        {
            var st = _s.Settings.Current;
            return $"זמן אימות נוכחי: {st.VerificationDelaySeconds} שניות · זמן מינימלי בין שינויים: {st.MinSecondsBetweenChanges} שניות";
        }
    }

    private void Simulate(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return;
        exe = exe.Trim();
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
        if (!IsEnabled) IsEnabled = true;
        _s.Coordinator.FakeProvider.SetForeground(exe);
        _s.Coordinator.RequestImmediateDetection();
        CurrentFake = exe;
    }
}

// ═══════════════════════════ הגדרות ═══════════════════════════

public sealed class SettingsViewModel : ObservableObject, IPageViewModel
{
    private readonly AppServices _s;

    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand CheckUpdatesCommand { get; }

    public string VersionText => $"גרסה {_s.Updates.CurrentVersion.ToString(3)}" +
                                 (_s.Updates.Enabled ? "" : " · עדכונים אוטומטיים לא הוגדרו בגרסה הזו");
    public bool UpdatesEnabled => _s.Updates.Enabled;

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }

    public SettingsViewModel(AppServices services)
    {
        _s = services;
        CheckUpdatesCommand = new AsyncRelayCommand(async () =>
        {
            UpdateStatus = "בודק…";
            var u = await _s.Updates.CheckAsync();
            UpdateStatus = u is null ? "יש לך את הגרסה האחרונה ✓" : $"גרסה {u.Tag} זמינה – לחץ \"עדכן עכשיו\" בלוח הבקרה";
        }, () => _s.Updates.Enabled);
        SaveCommand = new RelayCommand(Save);
        ResetCommand = new RelayCommand(() => { Load(new AppSettings
        {
            CurrentCategory = _s.Settings.Current.CurrentCategory,
            KickLiveMode = _s.Settings.Current.KickLiveMode,
        }); SavedText = "ברירות המחדל נטענו – לחץ שמור כדי להחיל"; });
        OpenDataFolderCommand = new RelayCommand(() => Shell.OpenFolder(_s.DataDirectory));
        Load(_s.Settings.Current);
    }

    public void OnNavigatedTo()
    {
        Load(_s.Settings.Current);
        SavedText = "";
    }

    public string DataDirectory => _s.DataDirectory;

    private string _detectionInterval = "";
    public string DetectionInterval { get => _detectionInterval; set => SetProperty(ref _detectionInterval, value); }

    private string _verificationDelay = "";
    public string VerificationDelay { get => _verificationDelay; set => SetProperty(ref _verificationDelay, value); }

    private string _minBetween = "";
    public string MinBetweenChanges { get => _minBetween; set => SetProperty(ref _minBetween, value); }

    private string _returnMinutes = "";
    public string ReturnToDefaultMinutes { get => _returnMinutes; set => SetProperty(ref _returnMinutes, value); }

    private string _defaultCategory = "";
    public string DefaultCategory { get => _defaultCategory; set => SetProperty(ref _defaultCategory, value); }

    private bool _returnEnabled;
    public bool ReturnToDefaultEnabled { get => _returnEnabled; set => SetProperty(ref _returnEnabled, value); }

    private bool _startWithWindows;
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }

    private bool _minimizeToTray;
    public bool MinimizeToTray { get => _minimizeToTray; set => SetProperty(ref _minimizeToTray, value); }

    private bool _showNotifications;
    public bool ShowNotifications { get => _showNotifications; set => SetProperty(ref _showNotifications, value); }

    private bool _autoEnabled;
    public bool AutoCategoryEnabled { get => _autoEnabled; set => SetProperty(ref _autoEnabled, value); }

    private bool _autoDetect;
    public bool AutoDetectGames { get => _autoDetect; set => SetProperty(ref _autoDetect, value); }

    private string _extraFolders = "";
    /// <summary>תיקייה בכל שורה.</summary>
    public string ExtraGameFolders { get => _extraFolders; set => SetProperty(ref _extraFolders, value); }

    private bool _fallbackEnabled;
    public bool FallbackCategoryEnabled { get => _fallbackEnabled; set => SetProperty(ref _fallbackEnabled, value); }

    private string _fallbackCategory = "";
    public string FallbackCategory { get => _fallbackCategory; set => SetProperty(ref _fallbackCategory, value); }

    private bool _saveLogs;
    public bool SaveLogsToFile { get => _saveLogs; set => SetProperty(ref _saveLogs, value); }

    private string _savedText = "";
    public string SavedText { get => _savedText; private set => SetProperty(ref _savedText, value); }

    private void Load(AppSettings s)
    {
        DetectionInterval = s.DetectionIntervalSeconds.ToString();
        VerificationDelay = s.VerificationDelaySeconds.ToString();
        MinBetweenChanges = s.MinSecondsBetweenChanges.ToString();
        ReturnToDefaultMinutes = s.ReturnToDefaultMinutes.ToString();
        DefaultCategory = s.DefaultCategory;
        ReturnToDefaultEnabled = s.ReturnToDefaultEnabled;
        StartWithWindows = s.StartWithWindows;
        MinimizeToTray = s.MinimizeToTray;
        ShowNotifications = s.ShowNotifications;
        AutoCategoryEnabled = s.AutoCategoryEnabled;
        SaveLogsToFile = s.SaveLogsToFile;
        FallbackCategoryEnabled = s.FallbackCategoryEnabled;
        FallbackCategory = s.FallbackCategory;
        AutoDetectGames = s.AutoDetectGames;
        ExtraGameFolders = string.Join(Environment.NewLine, s.ExtraGameFolders);
    }

    private void Save()
    {
        if (!TryInt(DetectionInterval, 1, 30, "מרווח זיהוי", out var interval)) return;
        if (!TryInt(VerificationDelay, 3, 600, "זמן אימות", out var delay)) return;
        if (!TryInt(MinBetweenChanges, 0, 3600, "זמן מינימלי בין שינויים", out var between)) return;
        if (!TryInt(ReturnToDefaultMinutes, 1, 240, "דקות עד חזרה לברירת מחדל", out var ret)) return;

        var s = _s.Settings.Current;
        var startupChanged = s.StartWithWindows != StartWithWindows;

        s.DetectionIntervalSeconds = interval;
        s.VerificationDelaySeconds = delay;
        s.MinSecondsBetweenChanges = between;
        s.ReturnToDefaultMinutes = ret;
        s.DefaultCategory = string.IsNullOrWhiteSpace(DefaultCategory) ? "Just Chatting" : DefaultCategory.Trim();
        s.ReturnToDefaultEnabled = ReturnToDefaultEnabled;
        s.StartWithWindows = StartWithWindows;
        s.MinimizeToTray = MinimizeToTray;
        s.ShowNotifications = ShowNotifications;
        s.AutoCategoryEnabled = AutoCategoryEnabled;
        s.SaveLogsToFile = SaveLogsToFile;
        s.FallbackCategoryEnabled = FallbackCategoryEnabled;
        s.FallbackCategory = string.IsNullOrWhiteSpace(FallbackCategory) ? "Games + Demos" : FallbackCategory.Trim();
        s.AutoDetectGames = AutoDetectGames;
        s.ExtraGameFolders = (ExtraGameFolders ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _s.Settings.Save(s);

        if (startupChanged) App.TryApplyStartup(StartWithWindows);

        _s.Log.Log(LogLevel.Info, "ההגדרות נשמרו");
        SavedText = $"נשמר ב-{DateTime.Now:HH:mm:ss}";
    }

    private static bool TryInt(string text, int min, int max, string name, out int value)
    {
        if (int.TryParse(text?.Trim(), out value) && value >= min && value <= max) return true;
        DialogService.ShowError($"{name}: יש להזין מספר שלם בין {min} ל-{max}.");
        return false;
    }
}

// ═══════════════════════════ עזר ═══════════════════════════

internal static class Shell
{
    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { DialogService.ShowError(ex.Message); }
    }

    public static void OpenFolderSelecting(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { DialogService.ShowError(ex.Message); }
    }
}
