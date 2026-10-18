using System.Windows;
using AutoCategory.App.Views;
using AutoCategory.Core.Models;

namespace AutoCategory.App;

/// <summary>חלונות דו-שיח (הוספת כלל, קלט טקסט, הודעות).</summary>
public static class DialogService
{
    private const MessageBoxOptions Rtl = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;

    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    /// <summary>פותח את חלון הכלל. מחזיר את הכלל החדש, או null אם בוטל.</summary>
    public static GameMapping? EditMapping(GameMapping? existing, string dialogTitle, IEnumerable<string> knownCategories, bool siteMode = false)
    {
        var dlg = new MappingDialog(existing, dialogTitle, knownCategories, siteMode);
        var owner = Owner;
        if (owner is not null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    /// <summary>עריכת כלל אתר (משתמש באותו חלון, במצב "אתר").</summary>
    public static SiteRule? EditSite(SiteRule? existing, string dialogTitle, IEnumerable<string> knownCategories)
    {
        var asMapping = existing is null ? null : new GameMapping
        {
            Executable = existing.Keyword, Category = existing.Category, KickCategoryId = existing.KickCategoryId, Notes = existing.Notes,
        };
        var r = EditMapping(asMapping, dialogTitle, knownCategories, siteMode: true);
        return r is null ? null : new SiteRule { Keyword = r.Executable, Category = r.Category, KickCategoryId = r.KickCategoryId, Notes = r.Notes };
    }

    public static string? Prompt(string title, string message, string initialValue = "")
    {
        var dlg = new InputDialog(title, message, initialValue);
        var owner = Owner;
        if (owner is not null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    public static bool Confirm(string message, string title = "אישור") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, Rtl)
        == MessageBoxResult.Yes;

    public static void ShowError(string message) =>
        MessageBox.Show(message, "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, Rtl);

    public static void ShowInfo(string message, string title = "Catego") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, Rtl);
}
