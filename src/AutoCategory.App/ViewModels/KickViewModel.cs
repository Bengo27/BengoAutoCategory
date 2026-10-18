using System.Text;
using System.Windows;
using System.Windows.Input;
using AutoCategory.Core.Models;
using AutoCategory.Core.Platforms.Kick;

namespace AutoCategory.App.ViewModels;

public sealed class KickViewModel : ObservableObject, IPageViewModel
{
    private readonly AppServices _s;
    private CancellationTokenSource? _connectCts;

    public ICommand SaveCredentialsCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand CancelConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CheckRulesCommand { get; }
    public ICommand CopyRedirectCommand { get; }
    public ICommand OpenDeveloperPageCommand { get; }
    public ICommand OpenChannelCommand { get; }
    public ICommand ShowOwnAppSetupCommand { get; }
    public ICommand UseBuiltInCommand { get; }

    public KickViewModel(AppServices services)
    {
        _s = services;
        _clientId = _s.Kick.ClientId ?? "";
        _liveMode = _s.Settings.Current.KickLiveMode;

        SaveCredentialsCommand = new RelayCommand(SaveCredentials, () => !IsBusy);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && _s.Kick.HasCredentials);
        CancelConnectCommand = new RelayCommand(() => _connectCts?.Cancel(), () => IsBusy);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => !IsBusy && _s.Kick.IsConnected);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && _s.Kick.IsConnected);
        CheckRulesCommand = new AsyncRelayCommand(CheckRulesAsync, () => !IsBusy && _s.Kick.IsConnected);
        CopyRedirectCommand = new RelayCommand(() =>
        {
            try { Clipboard.SetText(RedirectUri); Message = "הכתובת הועתקה ✓"; } catch { /* clipboard busy */ }
        });
        OpenDeveloperPageCommand = new RelayCommand(() => AppServices.OpenInBrowser("https://kick.com/settings/developer"));
        OpenChannelCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(_s.Kick.ChannelSlug)) AppServices.OpenInBrowser("https://kick.com/" + _s.Kick.ChannelSlug);
        });

        ShowOwnAppSetupCommand = new RelayCommand(() => { _forceSetup = true; RefreshState(); });
        UseBuiltInCommand = new AsyncRelayCommand(async () =>
        {
            if (!DialogService.Confirm("לחזור להתחברות המובנית? פרטי האפליקציה שלך יימחקו ותצטרך להתחבר מחדש.", "התחברות מובנית")) return;
            await _s.Kick.UseBuiltInAppAsync();
            _forceSetup = false;
            ClientId = "";
            Message = "חזרת להתחברות המובנית. לחץ \"התחבר לקיק\".";
            RefreshState();
        }, () => !IsBusy && _s.Kick.UsesOwnApp && _s.Kick.BrokerAvailable);

        _s.Kick.ConnectionChanged += () => Ui(RefreshState);
        _s.KickSync.ChannelUpdated += ch => Ui(() => ApplyChannel(ch));
        if (_s.KickSync.LastChannel is { } c) ApplyChannel(c);
        RefreshState();
    }

    public void OnNavigatedTo()
    {
        LiveMode = _s.Settings.Current.KickLiveMode;
        RefreshState();
    }

    public string RedirectUri => KickEndpoints.RedirectUri;
    public string ScopesText => KickEndpoints.Scopes;

    // ───────────── פרטי האפליקציה ─────────────

    private string _clientId;
    public string ClientId { get => _clientId; set => SetProperty(ref _clientId, value); }

    /// <summary>נקבע מה-PasswordBox (לא נשמר בזיכרון של התצוגה).</summary>
    public string ClientSecret { private get; set; } = "";

    public bool HasCredentials => _s.Kick.HasCredentials;

    private bool _forceSetup;

    /// <summary>להציג את ההגדרה הידנית (אפליקציה משלך)? לא צריך כשיש התחברות מובנית.</summary>
    public bool ShowSetup => !_s.Kick.BrokerAvailable || _s.Kick.UsesOwnApp || _forceSetup;
    public bool ShowAdvancedLink => _s.Kick.BrokerAvailable && !ShowSetup;
    public bool ShowBackToBuiltIn => _s.Kick.BrokerAvailable && _s.Kick.UsesOwnApp;

    /// <summary>אחרי שמירה – התצוגה מנקה את שדה הסוד.</summary>
    public event Action? CredentialsSaved;
    public string CredentialsText => _s.Kick.HasCredentials ? "נשמרו (מוצפנים במחשב הזה) ✓" : "עוד לא הוזנו";

    // ───────────── מצב חיבור ─────────────

    public bool IsConnected => _s.Kick.IsConnected;
    public bool IsNotConnected => !_s.Kick.IsConnected;

    private string _statusTitle = "";
    public string StatusTitle { get => _statusTitle; private set => SetProperty(ref _statusTitle, value); }

    private string _channelText = "";
    public string ChannelText { get => _channelText; private set => SetProperty(ref _channelText, value); }

    private string _kickCategory = "—";
    public string KickCategory { get => _kickCategory; private set => SetProperty(ref _kickCategory, value); }

    private string _liveText = "";
    public string LiveText { get => _liveText; private set => SetProperty(ref _liveText, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested(); } }

    private string _message = "";
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    private string _rulesReport = "";
    public string RulesReport { get => _rulesReport; private set => SetProperty(ref _rulesReport, value); }

    private bool _liveMode;
    public bool LiveMode
    {
        get => _liveMode;
        set
        {
            if (!SetProperty(ref _liveMode, value)) return;
            if (_s.Settings.Current.KickLiveMode == value) return;
            _s.Settings.Update(st => st.KickLiveMode = value);
            _s.Log.Log(LogLevel.Info, value ? "שינוי קטגוריה אמיתי בקיק הופעל" : "שינוי בקיק כובה – מצב סימולציה");
        }
    }

    // ───────────── פעולות ─────────────

    private void SaveCredentials()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            Message = "יש להזין גם Client ID וגם Client Secret.";
            return;
        }
        try
        {
            _s.Kick.SetCredentials(ClientId, ClientSecret);
            ClientSecret = "";
            CredentialsSaved?.Invoke();
            Message = "הפרטים נשמרו. עכשיו לחץ \"התחבר לקיק\".";
            _s.Log.Log(LogLevel.Info, "פרטי אפליקציית הקיק נשמרו");
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        RefreshState();
    }

    private async Task ConnectAsync()
    {
        IsBusy = true;
        Message = "נפתח דף ההתחברות של קיק בדפדפן. אשר שם את ההרשאות וחזור לכאן…";
        _connectCts = new CancellationTokenSource();
        try
        {
            var name = await _s.Kick.ConnectAsync(_connectCts.Token);
            Message = $"התחברת בהצלחה בתור {name} ✓";
            await RefreshAsyncCore();
        }
        catch (OperationCanceledException)
        {
            Message = "ההתחברות בוטלה.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            _s.Log.Log(LogLevel.Warning, "ההתחברות לקיק נכשלה", ex.Message);
        }
        finally
        {
            _connectCts.Dispose();
            _connectCts = null;
            IsBusy = false;
            RefreshState();
        }
    }

    private async Task DisconnectAsync()
    {
        if (!DialogService.Confirm("להתנתק מקיק? התוכנה תחזור למצב סימולציה.", "התנתקות")) return;
        IsBusy = true;
        try { await _s.Kick.DisconnectAsync(); Message = "התנתקת מקיק."; }
        finally { IsBusy = false; RefreshState(); }
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try { await RefreshAsyncCore(); }
        finally { IsBusy = false; }
    }

    private async Task RefreshAsyncCore()
    {
        await _s.KickSync.SyncNowAsync();
        if (_s.KickSync.LastChannel is { } c) ApplyChannel(c);
        RefreshState();
    }

    /// <summary>
    /// עובר על כל הקטגוריות בכללים מול קיק: שמות מדויקים נשמרים; שמות שנמצאו בגרסה אחרת
    /// (למשל GTA V) מוצעים לתיקון; מה שלא נמצא בכלל ישתמש בקטגוריית הגיבוי.
    /// </summary>
    private async Task CheckRulesAsync()
    {
        IsBusy = true;
        RulesReport = "עובר על הקטגוריות מול קיק…";
        try
        {
            var settings = _s.Settings.Current;
            var rules = _s.Mappings.GetMappings();
            var names = rules
                .Where(m => string.IsNullOrWhiteSpace(m.KickCategoryId))
                .Select(m => m.Category)
                .Append(settings.DefaultCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var exact = new List<string>();
            var fixes = new List<(string Ours, AutoCategory.Core.Platforms.Kick.KickCategory Kick)>();
            var missing = new List<string>();
            var i = 0;
            foreach (var name in names)
            {
                RulesReport = $"בודק {++i}/{names.Count}: {name}…";
                var (cat, isExact) = await _s.Kick.FindBestCategoryAsync(name);
                if (cat is null) missing.Add(name);
                else if (isExact || KickApiClient.SameName(cat.Name, name))
                {
                    exact.Add(name);
                    _s.Mappings.SetKickCategoryId(name, cat.Id);
                }
                else fixes.Add((name, cat));
            }

            // קטגוריית הגיבוי – לוודא שהיא קיימת
            string fallbackLine = "";
            if (settings.FallbackCategoryEnabled)
            {
                var (fb, _) = await _s.Kick.FindBestCategoryAsync(settings.FallbackCategory);
                fallbackLine = fb is null
                    ? $"⚠ קטגוריית הגיבוי \"{settings.FallbackCategory}\" לא נמצאה בקיק – תקן את השם בהגדרות."
                    : $"קטגוריית גיבוי: {fb.Name} ✓";
                if (fb is not null && !KickApiClient.SameName(fb.Name, settings.FallbackCategory))
                    _s.Settings.Update(st => st.FallbackCategory = fb.Name);
            }

            var applied = 0;
            if (fixes.Count > 0 && DialogService.Confirm(
                    "נמצאו בקיק בשם קצת שונה:\n\n" +
                    string.Join("\n", fixes.Select(f => $"• {f.Ours}  ←  {f.Kick.Name}")) +
                    "\n\nלעדכן את הכללים לשמות של קיק?", "תיקון שמות קטגוריות"))
            {
                foreach (var (ours, kick) in fixes)
                {
                    foreach (var m in _s.Mappings.GetMappings().Where(m => string.Equals(m.Category, ours, StringComparison.OrdinalIgnoreCase)))
                    {
                        var u = m.Clone();
                        u.Category = kick.Name;
                        u.KickCategoryId = kick.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        _s.Mappings.Replace(m, u);
                    }
                    if (string.Equals(_s.Engine.CurrentCategory, ours, StringComparison.OrdinalIgnoreCase))
                        _s.Engine.SetCurrentCategoryManually(kick.Name);
                    if (string.Equals(_s.Settings.Current.DefaultCategory, ours, StringComparison.OrdinalIgnoreCase))
                        _s.Settings.Update(st => st.DefaultCategory = kick.Name);
                    applied++;
                }
                _s.Log.Log(LogLevel.Info, $"עודכנו {applied} שמות קטגוריות לשמות של קיק");
            }

            var linked = _s.Mappings.GetMappings().Count(m => !string.IsNullOrWhiteSpace(m.KickCategoryId));
            var total = _s.Mappings.GetMappings().Count;
            var sb = new StringBuilder();
            sb.AppendLine($"✓ כללים מקושרים לקיק: {linked} מתוך {total}");
            if (applied > 0) sb.AppendLine($"✓ תוקנו {applied} שמות: " + string.Join(", ", fixes.Select(f => f.Kick.Name)));
            else if (fixes.Count > 0) sb.AppendLine($"• {fixes.Count} שמות לא תוקנו (בחרת לא לעדכן)");
            if (missing.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"לא נמצאו בקיק ({missing.Count}): " + string.Join(", ", missing));
                sb.AppendLine(settings.FallbackCategoryEnabled
                    ? $"   ← יעברו אוטומטית ל-{_s.Settings.Current.FallbackCategory}. אפשר גם לערוך את הכלל ולבחור ידנית."
                    : "   ← ערוך את הכלל ובחר קטגוריה ב\"חפש בקיק\", או הפעל קטגוריית גיבוי בהגדרות.");
            }
            if (fallbackLine.Length > 0) { sb.AppendLine(); sb.AppendLine(fallbackLine); }
            if (missing.Count == 0 && fixes.Count == applied) sb.AppendLine("הכול תקין 👍");
            RulesReport = sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            RulesReport = "הבדיקה נכשלה: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ───────────── תצוגה ─────────────

    private void RefreshState()
    {
        var k = _s.Kick;
        if (k.IsConnected)
        {
            StatusTitle = $"מחובר לקיק בתור {k.AccountName}";
            ChannelText = string.IsNullOrEmpty(k.ChannelSlug) ? "" : "kick.com/" + k.ChannelSlug;
        }
        else
        {
            StatusTitle = k.HasCredentials ? "לא מחובר – לחץ \"התחבר לקיק\"" : "לא מחובר – קודם צריך להזין את פרטי האפליקציה (שלב 2 למטה)";
            if (k.UsesBroker) StatusTitle = "לא מחובר – לחץ \"התחבר לקיק\" ואשר בדפדפן";
            ChannelText = "";
            KickCategory = "—";
            LiveText = "";
        }
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsNotConnected));
        OnPropertyChanged(nameof(HasCredentials));
        OnPropertyChanged(nameof(CredentialsText));
        OnPropertyChanged(nameof(ShowSetup));
        OnPropertyChanged(nameof(ShowAdvancedLink));
        OnPropertyChanged(nameof(ShowBackToBuiltIn));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyChannel(KickChannelInfo c)
    {
        KickCategory = c.Category?.Name ?? (c.IsLive ? "—" : "— (קיק מציג קטגוריה רק בזמן שידור)");
        LiveText = c.IsLive ? "🔴 בשידור חי" : "לא בשידור כרגע";
    }

    private static void Ui(Action a) => Application.Current?.Dispatcher.InvokeAsync(a);
}
