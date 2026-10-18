using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AutoCategory.Core.Models;
using AutoCategory.Core.Storage;

namespace AutoCategory.App.ViewModels;

/// <summary>עמוד "כללי אתרים": מילת מפתח בכותרת הלשונית → קטגוריה.</summary>
public sealed class SiteRulesViewModel : ObservableObject, IPageViewModel
{
    private readonly AppServices _s;

    public ObservableCollection<SiteRule> Rules { get; } = new();
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand AddCasinoDefaultsCommand { get; }

    public string BrowsersText { get; } = "דפדפנים נתמכים: " + string.Join(", ",
        DefaultData.Browsers.Select(b => b.Replace(".exe", "")).OrderBy(b => b));

    public SiteRulesViewModel(AppServices services)
    {
        _s = services;
        AddCommand = new RelayCommand(Add);
        EditCommand = new RelayCommand(Edit, () => Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null);
        AddCasinoDefaultsCommand = new RelayCommand(() =>
        {
            foreach (var r in DefaultData.CreateSiteRules())
                if (!_s.Mappings.GetSiteRules().Any(x => x.SameKeyword(r))) _s.Mappings.AddOrUpdateSite(r);
            _s.Log.Log(LogLevel.Info, "נוספו כללי הקזינו המוכרים");
        });
        _s.Mappings.Changed += () => Application.Current?.Dispatcher.InvokeAsync(Reload);
        Reload();
    }

    public void OnNavigatedTo() => Reload();

    private SiteRule? _selected;
    public SiteRule? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    private string _countText = "";
    public string CountText { get => _countText; private set => SetProperty(ref _countText, value); }

    private void Reload()
    {
        var keep = Selected?.Keyword;
        Rules.Clear();
        foreach (var r in _s.Mappings.GetSiteRules()
                     .OrderBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => r.Keyword, StringComparer.OrdinalIgnoreCase))
            Rules.Add(r);
        Selected = Rules.FirstOrDefault(r => r.Keyword == keep);
        CountText = $"{Rules.Count} כללים";
    }

    private void Add()
    {
        var r = DialogService.EditSite(null, "כלל אתר חדש", _s.Mappings.KnownCategories());
        if (r is null) return;
        _s.Mappings.AddOrUpdateSite(r);
        _s.Log.Log(LogLevel.Info, $"נוסף כלל אתר: \"{r.Keyword}\" → {r.Category}");
    }

    public void Edit()
    {
        if (Selected is null) return;
        var original = Selected;
        var r = DialogService.EditSite(original.Clone(), "עריכת כלל אתר", _s.Mappings.KnownCategories());
        if (r is null) return;
        _s.Mappings.ReplaceSite(original, r);
        _s.Log.Log(LogLevel.Info, $"כלל אתר עודכן: \"{r.Keyword}\" → {r.Category}");
    }

    private void Delete()
    {
        if (Selected is null) return;
        var r = Selected;
        if (!DialogService.Confirm($"למחוק את הכלל\n\"{r.Keyword}\" → {r.Category}?", "מחיקת כלל אתר")) return;
        _s.Mappings.RemoveSite(r);
        _s.Log.Log(LogLevel.Info, $"כלל אתר נמחק: \"{r.Keyword}\"");
    }
}
