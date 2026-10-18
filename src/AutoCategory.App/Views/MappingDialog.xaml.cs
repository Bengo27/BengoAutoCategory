using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoCategory.Core.Models;

namespace AutoCategory.App.Views;

public partial class MappingDialog : Window
{
    public GameMapping? Result { get; private set; }

    /// <summary>שם הקטגוריה שהמזהה של קיק שייך לו. אם המשתמש משנה את השם – המזהה כבר לא תקף.</summary>
    private string? _linkedName;
    private readonly bool _siteMode;

    public MappingDialog(GameMapping? existing, string title, IEnumerable<string> knownCategories, bool siteMode = false)
    {
        InitializeComponent();
        Title = title;
        HeaderText.Text = title;
        _siteMode = siteMode;
        if (siteMode)
        {
            SubtitleText.Text = "כשהדפדפן בחזית וכותרת הלשונית מכילה את המילה הזו, התוכנה תעבור לקטגוריה שתבחר.";
            ExeLabel.Text = "מילת מפתח בכותרת הלשונית (למשל Stake)";
            AdvancedExpander.Visibility = Visibility.Collapsed;
        }

        if (existing is not null)
        {
            ExeBox.Text = existing.Executable;
            CategoryBox.Text = existing.Category;
            TitleBox.Text = existing.WindowTitleContains ?? "";
            AmbiguousBox.IsChecked = existing.IsAmbiguous;
            KickIdBox.Text = existing.KickCategoryId ?? "";
            NotesBox.Text = existing.Notes ?? "";
            if (!string.IsNullOrWhiteSpace(existing.KickCategoryId)) _linkedName = existing.Category;
        }

        foreach (var cat in knownCategories.Take(18))
        {
            var b = new Button
            {
                Content = cat,
                Style = (Style)FindResource("GhostButton"),
                Margin = new Thickness(2),
                FlowDirection = FlowDirection.LeftToRight,
            };
            b.Click += (_, _) => CategoryBox.Text = cat;
            KnownCategoriesPanel.Children.Add(b);
        }

        var kick = App.Services.Kick;
        if (!kick.IsConnected)
        {
            KickSearchStatus.Text = "התחבר לקיק (בעמוד \"חיבור לקיק\") כדי לחפש את שם הקטגוריה המדויק.";
            KickSearchBox.IsEnabled = false;
        }
        else
        {
            KickSearchStatus.Text = string.IsNullOrWhiteSpace(KickIdBox.Text)
                ? "בחר תוצאה כדי לקשר את הכלל לקטגוריה המדויקת בקיק."
                : $"מקושר לקטגוריה בקיק (מזהה {KickIdBox.Text}).";
        }
        KickSearchBox.Text = CategoryBox.Text;

        Loaded += (_, _) =>
        {
            var box = string.IsNullOrWhiteSpace(ExeBox.Text) ? ExeBox : CategoryBox;
            box.Focus();
            box.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var exe = ExeBox.Text.Trim();
        var cat = CategoryBox.Text.Trim();

        if (exe.Length == 0) { ShowError(_siteMode ? "יש להזין מילת מפתח (למשל Stake)." : "יש להזין שם קובץ הרצה (למשל eldenring.exe)."); return; }
        if (cat.Length == 0) { ShowError("יש להזין שם קטגוריה (למשל Elden Ring)."); return; }

        Result = new GameMapping
        {
            Executable = exe,
            Category = cat,
            WindowTitleContains = string.IsNullOrWhiteSpace(TitleBox.Text) ? null : TitleBox.Text.Trim(),
            IsAmbiguous = AmbiguousBox.IsChecked == true,
            KickCategoryId = string.IsNullOrWhiteSpace(KickIdBox.Text) ? null : KickIdBox.Text.Trim(),
            Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
        };
        DialogResult = true;
    }

    private void KickSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true; // לא לשמור את החלון
        KickSearch_Click(sender, e);
    }

    private async void KickSearch_Click(object sender, RoutedEventArgs e)
    {
        var kick = App.Services.Kick;
        if (!kick.IsConnected) return;
        var q = KickSearchBox.Text.Trim();
        if (q.Length < 2) { KickSearchStatus.Text = "הקלד לפחות 2 תווים."; return; }

        KickSearchStatus.Text = "מחפש…";
        KickResultsPanel.Children.Clear();
        try
        {
            var results = await kick.SearchCategoriesAsync(q);
            if (results.Count == 0) { KickSearchStatus.Text = "לא נמצאו קטגוריות. נסה שם אחר (באנגלית)."; return; }
            KickSearchStatus.Text = $"נמצאו {results.Count}. לחץ כדי לבחור:";
            foreach (var cat in results.Take(24))
            {
                var b = new Button
                {
                    Content = cat.Name,
                    Style = (Style)FindResource("BaseButton"),
                    Margin = new Thickness(4),
                    Padding = new Thickness(10, 5, 10, 5),
                    FlowDirection = FlowDirection.LeftToRight,
                    ToolTip = $"מזהה בקיק: {cat.Id}",
                };
                b.Click += (_, _) =>
                {
                    _linkedName = cat.Name;
                    CategoryBox.Text = cat.Name;
                    KickIdBox.Text = cat.Id.ToString();
                    KickSearchStatus.Text = $"נבחר: {cat.Name} (מזהה {cat.Id}) ✓";
                };
                KickResultsPanel.Children.Add(b);
            }
        }
        catch (Exception ex)
        {
            KickSearchStatus.Text = "החיפוש נכשל: " + ex.Message;
        }
    }

    private void CategoryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_linkedName is null || KickIdBox is null) return;
        if (string.Equals(CategoryBox.Text.Trim(), _linkedName, StringComparison.OrdinalIgnoreCase)) return;
        // השם שונה ידנית – המזהה הישן כבר לא מתאים
        _linkedName = null;
        KickIdBox.Text = "";
        if (KickSearchStatus is not null) KickSearchStatus.Text = "שם הקטגוריה שונה – חפש שוב בקיק כדי לקשר.";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
