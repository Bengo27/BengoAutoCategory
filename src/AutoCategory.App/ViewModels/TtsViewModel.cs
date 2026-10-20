using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using AutoCategory.Core.Platforms.Kick;
using AutoCategory.Core.Tts;

namespace AutoCategory.App.ViewModels;

/// <summary>שורה בתור ההקראות.</summary>
public sealed class TtsItemRow
{
    public TtsItemRow(TtsItem item) => Item = item;
    public TtsItem Item { get; }
    public string User => Item.User;
    public string Text => Item.Text;
    public string Time => Item.ReceivedAtUtc.ToLocalTime().ToString("HH:mm");
    public string StatusText => Item.Note is null || Item.Status is TtsStatus.Waiting or TtsStatus.Playing
        ? Item.StatusText : $"{Item.StatusText} · {Item.Note}";
    public bool CanApprove => Item.Status == TtsStatus.AwaitingApproval;
    public bool CanReject => Item.IsOpen && !Item.IsTest;
    public bool IsPlaying => Item.Status == TtsStatus.Playing;
    public bool IsFinished => !Item.IsOpen && !IsPlaying;
}

/// <summary>עמוד "הקראה (TTS)".</summary>
public sealed class TtsViewModel : ObservableObject, IPageViewModel
{
    private readonly AppServices _s;
    private TtsService Tts => _s.Tts;

    public ObservableCollection<TtsItemRow> Queue { get; } = new();

    public ICommand ToggleEnabledCommand { get; }
    public ICommand EnsureRewardCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand TestVoiceCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand RejectCommand { get; }
    public ICommand CopyWidgetUrlCommand { get; }
    public ICommand OpenWidgetPreviewCommand { get; }
    public ICommand GoToKickCommand { get; }
    public ICommand ResetBannedWordsCommand { get; }

    private readonly Action _goToKick;

    public TtsViewModel(AppServices services, Action goToKick)
    {
        _s = services;
        _goToKick = goToKick;

        ToggleEnabledCommand = new AsyncRelayCommand(ToggleEnabledAsync);
        EnsureRewardCommand = new AsyncRelayCommand(EnsureRewardAsync, () => _s.Kick.IsConnected);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        TestVoiceCommand = new RelayCommand(() =>
        {
            SaveVoiceOnly();
            Tts.EnqueueTest(string.IsNullOrWhiteSpace(TestText) ? "בדיקת קול" : TestText);
        });
        SkipCommand = new RelayCommand(() => Tts.SkipCurrent(), () => Tts.Current is not null);
        TogglePauseCommand = new RelayCommand(() => { Tts.QueuePaused = !Tts.QueuePaused; Refresh(); });
        ApproveCommand = new RelayCommand(p => { if (p is TtsItemRow r) Tts.Approve(r.Item); });
        RejectCommand = new RelayCommand(async p => { if (p is TtsItemRow r) await Tts.RejectAsync(r.Item); });
        CopyWidgetUrlCommand = new RelayCommand(() =>
        {
            try { Clipboard.SetText(WidgetUrl); Message = "הכתובת הועתקה ✓ – הדבק אותה ב-OBS ב-Browser Source"; } catch { }
        });
        OpenWidgetPreviewCommand = new RelayCommand(() => AppServices.OpenInBrowser(WidgetUrl + "?demo=1"));
        GoToKickCommand = new RelayCommand(() => _goToKick());
        ResetBannedWordsCommand = new RelayCommand(() => BannedWords = string.Join(Environment.NewLine, TtsFilter.DefaultBannedWords));

        Tts.Changed += () => Application.Current?.Dispatcher.InvokeAsync(Refresh);
        _s.Kick.ConnectionChanged += () => Application.Current?.Dispatcher.InvokeAsync(Refresh);
        LoadFromSettings();
        Refresh();
    }

    public void OnNavigatedTo()
    {
        LoadFromSettings();
        Refresh();
    }

    // ───────────────────────── מצב ─────────────────────────

    private bool _enabled;
    /// <summary>המתג הראשי (הפעלה / כיבוי של ההקראה).</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled) return;
            _enabled = value;
            OnPropertyChanged();
            ToggleEnabledCommand.Execute(null);
        }
    }

    private string _statusTitle = "";
    public string StatusTitle { get => _statusTitle; private set => SetProperty(ref _statusTitle, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private bool _needsKick;
    /// <summary>צריך להתחבר (או להתחבר מחדש) לקיק.</summary>
    public bool NeedsKick { get => _needsKick; private set => SetProperty(ref _needsKick, value); }

    private string _rewardButtonText = "צור פרס בקיק";
    public string RewardButtonText { get => _rewardButtonText; private set => SetProperty(ref _rewardButtonText, value); }

    private string _pauseButtonText = "השהה תור";
    public string PauseButtonText { get => _pauseButtonText; private set => SetProperty(ref _pauseButtonText, value); }

    private string _message = "";
    public string Message { get => _message; set => SetProperty(ref _message, value); }

    private string _queueSummary = "";
    public string QueueSummary { get => _queueSummary; private set => SetProperty(ref _queueSummary, value); }

    public string WidgetUrl => _s.TtsWidget.WidgetUrl;
    public bool WidgetRunning => _s.TtsWidget.IsRunning;

    private void Refresh()
    {
        var st = Tts.Settings.Current;
        if (_enabled != st.Enabled) { _enabled = st.Enabled; OnPropertyChanged(nameof(Enabled)); }
        var hasReward = !string.IsNullOrEmpty(st.RewardId);
        RewardButtonText = hasReward ? "עדכן את הפרס בקיק" : "צור פרס בקיק";
        PauseButtonText = Tts.QueuePaused ? "▶ המשך תור" : "⏸ השהה תור";

        if (!_s.Kick.IsConnected)
        {
            NeedsKick = true;
            StatusTitle = "לא מחובר לקיק";
            StatusText = "ההקראה עובדת דרך פרס נקודות בערוץ, אז צריך להתחבר לקיק קודם. (בדיקת קול עובדת גם בלי.)";
        }
        else if (!_s.Kick.HasRewardsScope)
        {
            NeedsKick = true;
            StatusTitle = "צריך להתחבר מחדש לקיק";
            StatusText = "ההתחברות שלך נעשתה לפני שהוספנו את ההקראה. בעמוד \"חיבור לקיק\": התנתק ← התחבר, ואשר את ההרשאה לפרסים.";
        }
        else if (!hasReward)
        {
            NeedsKick = false;
            StatusTitle = "עוד אין פרס הקראה בערוץ";
            StatusText = "בחר שם ומחיר למטה ולחץ \"צור פרס בקיק\". הפרס יופיע לצופים בתפריט הנקודות.";
        }
        else if (Tts.LastError is { } err)
        {
            NeedsKick = false;
            StatusTitle = "יש בעיה מול קיק";
            StatusText = err;
        }
        else
        {
            NeedsKick = false;
            StatusTitle = st.Enabled ? (Tts.QueuePaused ? "ההקראה פעילה · התור מושהה" : "ההקראה פעילה 🔊") : "ההקראה כבויה";
            StatusText = st.Enabled
                ? $"צופים יכולים לקנות \"{st.RewardTitle}\" ב-{st.RewardCost:N0} נקודות."
                : "הפרס בקיק מושהה – צופים לא יכולים לקנות אותו עד שתפעיל.";
        }

        Queue.Clear();
        var items = Tts.Items;
        foreach (var i in items.Where(i => i.Status == TtsStatus.Playing)) Queue.Add(new TtsItemRow(i));
        foreach (var i in items.Where(i => i.IsOpen)) Queue.Add(new TtsItemRow(i));
        foreach (var i in items.Where(i => !i.IsOpen && i.Status != TtsStatus.Playing).Reverse().Take(15)) Queue.Add(new TtsItemRow(i));
        var waiting = items.Count(i => i.IsOpen);
        QueueSummary = waiting == 0 ? "התור ריק" : $"{waiting} ממתינים";
        CommandManager.InvalidateRequerySuggested();
    }

    // ───────────────────────── הגדרות ─────────────────────────

    private string _rewardTitle = "";
    public string RewardTitle { get => _rewardTitle; set => SetProperty(ref _rewardTitle, value); }
    private string _rewardCost = "";
    public string RewardCost { get => _rewardCost; set => SetProperty(ref _rewardCost, value); }
    private string _rewardDescription = "";
    public string RewardDescription { get => _rewardDescription; set => SetProperty(ref _rewardDescription, value); }

    private string _voice = TtsVoices.Hila;
    public bool VoiceHila { get => _voice == TtsVoices.Hila; set { if (value) SetVoice(TtsVoices.Hila); } }
    public bool VoiceAvri { get => _voice == TtsVoices.Avri; set { if (value) SetVoice(TtsVoices.Avri); } }
    public bool VoiceWindows { get => _voice == TtsVoices.Windows; set { if (value) SetVoice(TtsVoices.Windows); } }
    private void SetVoice(string v)
    {
        _voice = v;
        OnPropertyChanged(nameof(VoiceHila));
        OnPropertyChanged(nameof(VoiceAvri));
        OnPropertyChanged(nameof(VoiceWindows));
    }

    private double _rate;
    public double Rate { get => _rate; set { if (SetProperty(ref _rate, Math.Round(value))) OnPropertyChanged(nameof(RateText)); } }
    public string RateText => _rate == 0 ? "רגילה" : (_rate > 0 ? $"+{_rate}%" : $"{_rate}%");

    private double _volume;
    public double Volume { get => _volume; set { if (SetProperty(ref _volume, Math.Round(value))) OnPropertyChanged(nameof(VolumeText)); } }
    public string VolumeText => $"{_volume}%";

    private string _testText = "בדיקה, אחת שתיים. ככה ישמעו ההודעות בשידור!";
    public string TestText { get => _testText; set => SetProperty(ref _testText, value); }

    private bool _playInWidget;
    public bool PlayInApp { get => !_playInWidget; set { if (value) SetPlayback(false); } }
    public bool PlayInWidget { get => _playInWidget; set { if (value) SetPlayback(true); } }
    private void SetPlayback(bool widget)
    {
        _playInWidget = widget;
        OnPropertyChanged(nameof(PlayInApp));
        OnPropertyChanged(nameof(PlayInWidget));
    }

    private string _template = "";
    public string Template { get => _template; set => SetProperty(ref _template, value); }
    private string _maxChars = "";
    public string MaxChars { get => _maxChars; set => SetProperty(ref _maxChars, value); }
    private bool _blockLinks;
    public bool BlockLinks { get => _blockLinks; set => SetProperty(ref _blockLinks, value); }
    private bool _manualApproval;
    public bool ManualApproval { get => _manualApproval; set => SetProperty(ref _manualApproval, value); }
    private string _maxAge = "";
    public string MaxAge { get => _maxAge; set => SetProperty(ref _maxAge, value); }
    private string _gap = "";
    public string Gap { get => _gap; set => SetProperty(ref _gap, value); }
    private string _bannedWords = "";
    public string BannedWords { get => _bannedWords; set => SetProperty(ref _bannedWords, value); }

    private string _savedText = "";
    public string SavedText { get => _savedText; private set => SetProperty(ref _savedText, value); }

    private void LoadFromSettings()
    {
        var s = Tts.Settings.Current;
        RewardTitle = s.RewardTitle;
        RewardCost = s.RewardCost.ToString(CultureInfo.InvariantCulture);
        RewardDescription = s.RewardDescription;
        SetVoice(s.Voice);
        Rate = s.RatePercent;
        Volume = s.VolumePercent;
        SetPlayback(s.Playback == TtsPlayback.Widget);
        Template = s.Template;
        MaxChars = s.MaxChars.ToString(CultureInfo.InvariantCulture);
        BlockLinks = s.BlockLinks;
        ManualApproval = s.ManualApproval;
        MaxAge = s.MaxAgeMinutes.ToString(CultureInfo.InvariantCulture);
        Gap = s.GapSeconds.ToString(CultureInfo.InvariantCulture);
        BannedWords = string.Join(Environment.NewLine, s.BannedWords);
        SavedText = "";
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text.Replace(",", "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    /// <summary>בדיקת קול שומרת רק את הקול/מהירות/עוצמה (כדי לשמוע מה שבחרת).</summary>
    private void SaveVoiceOnly() => Tts.Settings.Update(s =>
    {
        s.Voice = _voice;
        s.RatePercent = (int)_rate;
        s.VolumePercent = (int)_volume;
        s.Playback = _playInWidget ? TtsPlayback.Widget : TtsPlayback.App;
    });

    private async Task SaveAsync()
    {
        var before = Tts.Settings.Current;
        Tts.Settings.Update(s =>
        {
            s.RewardTitle = RewardTitle;
            s.RewardCost = ParseInt(RewardCost, s.RewardCost);
            s.RewardDescription = RewardDescription;
            s.Voice = _voice;
            s.RatePercent = (int)_rate;
            s.VolumePercent = (int)_volume;
            s.Playback = _playInWidget ? TtsPlayback.Widget : TtsPlayback.App;
            s.Template = Template;
            s.MaxChars = ParseInt(MaxChars, s.MaxChars);
            s.BlockLinks = BlockLinks;
            s.ManualApproval = ManualApproval;
            s.MaxAgeMinutes = ParseInt(MaxAge, s.MaxAgeMinutes);
            s.GapSeconds = ParseInt(Gap, s.GapSeconds);
            s.BannedWords = BannedWords.Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        });
        var after = Tts.Settings.Current;
        LoadFromSettings();
        SavedText = $"נשמר ב-{DateTime.Now:HH:mm:ss}";

        // השם / המחיר / התיאור השתנו – מעדכנים גם בקיק
        var rewardChanged = before.RewardTitle != after.RewardTitle || before.RewardCost != after.RewardCost ||
                            before.RewardDescription != after.RewardDescription;
        if (rewardChanged && !string.IsNullOrEmpty(after.RewardId) && _s.Kick.IsConnected)
        {
            await EnsureRewardAsync();
            SavedText += " · הפרס עודכן בקיק";
        }
        Refresh();
    }

    private async Task EnsureRewardAsync()
    {
        try
        {
            // לפני יצירה – לשמור את השם והמחיר שמופיעים במסך
            Tts.Settings.Update(s =>
            {
                s.RewardTitle = RewardTitle;
                s.RewardCost = ParseInt(RewardCost, s.RewardCost);
                s.RewardDescription = RewardDescription;
            });
            var r = await Tts.EnsureRewardAsync();
            Message = $"✓ הפרס \"{r.Title}\" ({r.Cost:N0} נקודות) מוכן בקיק" +
                      (Tts.Settings.Current.Enabled ? "" : " – הפעל את ההקראה כדי שצופים יוכלו לקנות אותו");
        }
        catch (KickException ex)
        {
            Message = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            Message = "אין חיבור לקיק: " + ex.Message;
        }
        Refresh();
    }

    private async Task ToggleEnabledAsync()
    {
        var turnOn = _enabled;
        if (turnOn == Tts.Settings.Current.Enabled) return;
        if (turnOn && string.IsNullOrEmpty(Tts.Settings.Current.RewardId) && _s.Kick.IsConnected && _s.Kick.HasRewardsScope)
        {
            await Tts.SetEnabledAsync(true);
            await EnsureRewardAsync();
        }
        else
        {
            await Tts.SetEnabledAsync(turnOn);
        }
        Message = "";
        Refresh();
    }
}
