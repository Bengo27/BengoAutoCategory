using AutoCategory.Core.Models;
using AutoCategory.Core.Storage;

namespace AutoCategory.Core.Services;

public interface ICategoryMappingService
{
    event Action? Changed;
    string FilePath { get; }

    IReadOnlyList<GameMapping> GetMappings();
    IReadOnlyList<string> GetIgnoreList();

    /// <summary>מחפש כלל שמתאים לקובץ ולכותרת החלון. מחזיר null אם אין.</summary>
    GameMapping? FindMapping(string executable, string? windowTitle);
    bool IsIgnored(string executable);
    bool HasAnyMappingFor(string executable);

    /// <summary>מוסיף כלל חדש, או מחליף כלל קיים עם אותו exe ואותו תנאי כותרת.</summary>
    void AddOrUpdate(GameMapping mapping);
    /// <summary>מחליף כלל קיים (לפי האובייקט המקורי) בכלל חדש.</summary>
    void Replace(GameMapping original, GameMapping updated);
    void Remove(GameMapping mapping);

    void AddIgnored(string executable);
    void RemoveIgnored(string executable);

    IReadOnlyList<string> KnownCategories();

    /// <summary>שומר מזהה קטגוריה של קיק לכל הכללים עם שם הקטגוריה הזה (שעוד אין להם מזהה).</summary>
    int SetKickCategoryId(string categoryName, long kickCategoryId);

    /// <summary>משנה שם קטגוריה בכל הכללים (משחקים ואתרים), ושומר את מזהה קיק.</summary>
    int RenameCategory(string oldName, string newName, string? kickCategoryId);

    // ── כללי אתרים ──
    IReadOnlyList<SiteRule> GetSiteRules();
    /// <summary>הכלל הראשון שמתאים לכותרת (אם הקובץ הוא דפדפן).</summary>
    SiteRule? FindSiteRule(string executable, string? windowTitle);
    bool IsBrowser(string executable);
    void AddOrUpdateSite(SiteRule rule);
    void ReplaceSite(SiteRule original, SiteRule updated);
    void RemoveSite(SiteRule rule);
}

/// <summary>
/// מסד הכללים המקומי (game_mappings.json): מיפויים + רשימת התעלמות.
/// בטוח לשימוש משני חוטים (ממשק + לולאת הזיהוי).
/// </summary>
public sealed class CategoryMappingService : ICategoryMappingService
{
    private readonly object _lock = new();
    private readonly ILoggingService? _log;
    private MappingDatabase _db;

    public event Action? Changed;
    public string FilePath { get; }

    public CategoryMappingService(string dataDirectory, ILoggingService? log = null)
    {
        _log = log;
        FilePath = Path.Combine(dataDirectory, "game_mappings.json");
        _db = LoadOrCreate();
    }

    private MappingDatabase LoadOrCreate()
    {
        try
        {
            var db = JsonStore.Load<MappingDatabase>(FilePath);
            if (db is not null)
            {
                db.Mappings ??= new();
                db.IgnoreList ??= new();
                db.Mappings.RemoveAll(m => string.IsNullOrWhiteSpace(m.Executable) || string.IsNullOrWhiteSpace(m.Category));
                // שדרוג קובץ ישן: מוסיפים את כללי האתרים של ברירת המחדל (קזינו)
                if (db.SiteRules is null)
                {
                    db.SiteRules = DefaultData.CreateSiteRules();
                    db.Version = DefaultData.CurrentVersion;
                    TrySave(db);
                }
                db.SiteRules.RemoveAll(r => string.IsNullOrWhiteSpace(r.Keyword) || string.IsNullOrWhiteSpace(r.Category));
                return db;
            }
        }
        catch (Exception ex)
        {
            JsonStore.BackupCorrupt(FilePath);
            _log?.Log(LogLevel.Warning, "קובץ הכללים פגום – נטענו כללי ברירת מחדל (עותק של הקובץ הישן נשמר)", ex.Message);
        }

        var fresh = DefaultData.CreateMappingDatabase();
        TrySave(fresh);
        return fresh;
    }

    public IReadOnlyList<GameMapping> GetMappings()
    {
        lock (_lock) return _db.Mappings.Select(m => m.Clone()).ToList();
    }

    public IReadOnlyList<string> GetIgnoreList()
    {
        lock (_lock) return _db.IgnoreList.ToList();
    }

    public GameMapping? FindMapping(string executable, string? windowTitle)
    {
        var exe = GameMapping.Normalize(executable);
        if (exe.Length == 0) return null;
        windowTitle ??= "";

        lock (_lock)
        {
            GameMapping? fallback = null;
            foreach (var m in _db.Mappings)
            {
                if (m.NormalizedExecutable != exe) continue;
                if (!string.IsNullOrWhiteSpace(m.WindowTitleContains))
                {
                    // כלל עם תנאי כותרת – הכי ספציפי, מנצח מיד
                    if (windowTitle.Contains(m.WindowTitleContains, StringComparison.OrdinalIgnoreCase))
                        return m.Clone();
                }
                else
                {
                    fallback ??= m;
                }
            }
            return fallback?.Clone();
        }
    }

    public bool HasAnyMappingFor(string executable)
    {
        var exe = GameMapping.Normalize(executable);
        lock (_lock) return _db.Mappings.Any(m => m.NormalizedExecutable == exe);
    }

    public bool IsIgnored(string executable)
    {
        var exe = GameMapping.Normalize(executable);
        if (exe.Length == 0) return false;
        lock (_lock) return _db.IgnoreList.Any(i => GameMapping.Normalize(i) == exe);
    }

    public void AddOrUpdate(GameMapping mapping)
    {
        Validate(mapping);
        var copy = Tidy(mapping);
        lock (_lock)
        {
            _db.Mappings.RemoveAll(m => SameRule(m, copy));
            _db.Mappings.Add(copy);
            // כלל מיפוי מנצח את רשימת ההתעלמות: מי שמיפה exe רוצה שיזוהה
            _db.IgnoreList.RemoveAll(i => GameMapping.Normalize(i) == copy.NormalizedExecutable);
            TrySave(_db);
        }
        Changed?.Invoke();
    }

    public void Replace(GameMapping original, GameMapping updated)
    {
        Validate(updated);
        var copy = Tidy(updated);
        lock (_lock)
        {
            // אם הכלל החדש מתנגש בכלל אחר קיים – הכלל האחר מוחלף
            _db.Mappings.RemoveAll(m => SameRule(m, copy) && !SameRule(m, original));
            var idx = _db.Mappings.FindIndex(m => SameRule(m, original));
            if (idx >= 0) _db.Mappings[idx] = copy; else _db.Mappings.Add(copy);
            _db.IgnoreList.RemoveAll(i => GameMapping.Normalize(i) == copy.NormalizedExecutable);
            TrySave(_db);
        }
        Changed?.Invoke();
    }

    public void Remove(GameMapping mapping)
    {
        lock (_lock)
        {
            _db.Mappings.RemoveAll(m => SameRule(m, mapping));
            TrySave(_db);
        }
        Changed?.Invoke();
    }

    public void AddIgnored(string executable)
    {
        var exe = GameMapping.Normalize(executable);
        if (exe.Length == 0) throw new ArgumentException("יש להזין שם קובץ");
        lock (_lock)
        {
            if (_db.IgnoreList.Any(i => GameMapping.Normalize(i) == exe)) return;
            _db.IgnoreList.Add(DisplayName(executable));
            TrySave(_db);
        }
        Changed?.Invoke();
    }

    public void RemoveIgnored(string executable)
    {
        var exe = GameMapping.Normalize(executable);
        lock (_lock)
        {
            _db.IgnoreList.RemoveAll(i => GameMapping.Normalize(i) == exe);
            TrySave(_db);
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<string> KnownCategories()
    {
        lock (_lock)
            return _db.Mappings.Select(m => m.Category).Concat((_db.SiteRules ?? new()).Select(r => r.Category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public int SetKickCategoryId(string categoryName, long kickCategoryId)
    {
        var count = 0;
        var id = kickCategoryId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock (_lock)
        {
            foreach (var m in _db.Mappings)
            {
                if (!string.Equals(m.Category.Trim(), categoryName.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(m.KickCategoryId)) continue;
                m.KickCategoryId = id;
                count++;
            }
            foreach (var r in _db.SiteRules ?? new())
            {
                if (!string.Equals(r.Category.Trim(), categoryName.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(r.KickCategoryId)) continue;
                r.KickCategoryId = id;
                count++;
            }
            if (count > 0) TrySave(_db);
        }
        if (count > 0) Changed?.Invoke();
        return count;
    }

    public int RenameCategory(string oldName, string newName, string? kickCategoryId)
    {
        var count = 0;
        lock (_lock)
        {
            foreach (var m in _db.Mappings.Where(m => string.Equals(m.Category, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                m.Category = newName.Trim();
                if (!string.IsNullOrWhiteSpace(kickCategoryId)) m.KickCategoryId = kickCategoryId;
                count++;
            }
            foreach (var r in (_db.SiteRules ?? new()).Where(r => string.Equals(r.Category, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                r.Category = newName.Trim();
                if (!string.IsNullOrWhiteSpace(kickCategoryId)) r.KickCategoryId = kickCategoryId;
                count++;
            }
            if (count > 0) TrySave(_db);
        }
        if (count > 0) Changed?.Invoke();
        return count;
    }

    // ───────────── כללי אתרים ─────────────

    public IReadOnlyList<SiteRule> GetSiteRules()
    {
        lock (_lock) return (_db.SiteRules ?? new()).Select(r => r.Clone()).ToList();
    }

    public bool IsBrowser(string executable) => DefaultData.Browsers.Contains(GameMapping.Normalize(executable));

    public SiteRule? FindSiteRule(string executable, string? windowTitle)
    {
        if (!IsBrowser(executable) || string.IsNullOrWhiteSpace(windowTitle)) return null;
        lock (_lock)
        {
            // מילת המפתח הארוכה ביותר קודם ("Stake.us" לפני "Stake")
            return (_db.SiteRules ?? new()).OrderByDescending(r => r.Keyword.Length)
                .FirstOrDefault(r => r.Matches(windowTitle))?.Clone();
        }
    }

    public void AddOrUpdateSite(SiteRule rule)
    {
        var c = TidySite(rule);
        lock (_lock)
        {
            _db.SiteRules ??= new();
            _db.SiteRules.RemoveAll(r => r.SameKeyword(c));
            _db.SiteRules.Add(c);
            TrySave(_db);
        }
        Changed?.Invoke();
    }

    public void ReplaceSite(SiteRule original, SiteRule updated)
    {
        var c = TidySite(updated);
        lock (_lock)
        {
            _db.SiteRules ??= new();
            _db.SiteRules.RemoveAll(r => r.SameKeyword(c) && !r.SameKeyword(original));
            var idx = _db.SiteRules.FindIndex(r => r.SameKeyword(original));
            if (idx >= 0) _db.SiteRules[idx] = c; else _db.SiteRules.Add(c);
            TrySave(_db);
        }
        Changed?.Invoke();
    }

    public void RemoveSite(SiteRule rule)
    {
        lock (_lock)
        {
            _db.SiteRules?.RemoveAll(r => r.SameKeyword(rule));
            TrySave(_db);
        }
        Changed?.Invoke();
    }

    private static SiteRule TidySite(SiteRule r)
    {
        if (string.IsNullOrWhiteSpace(r.Keyword)) throw new ArgumentException("יש להזין מילת מפתח (למשל Stake)");
        if (string.IsNullOrWhiteSpace(r.Category)) throw new ArgumentException("יש להזין קטגוריה");
        var c = r.Clone();
        c.Keyword = c.Keyword.Trim();
        c.Category = c.Category.Trim();
        c.KickCategoryId = string.IsNullOrWhiteSpace(c.KickCategoryId) ? null : c.KickCategoryId.Trim();
        c.Notes = string.IsNullOrWhiteSpace(c.Notes) ? null : c.Notes.Trim();
        return c;
    }

    private static void Validate(GameMapping m)
    {
        if (GameMapping.Normalize(m.Executable).Length == 0) throw new ArgumentException("יש להזין שם קובץ הרצה");
        if (string.IsNullOrWhiteSpace(m.Category)) throw new ArgumentException("יש להזין קטגוריה");
    }

    private static GameMapping Tidy(GameMapping m)
    {
        var c = m.Clone();
        c.Executable = DisplayName(c.Executable);
        c.Category = c.Category.Trim();
        c.WindowTitleContains = string.IsNullOrWhiteSpace(c.WindowTitleContains) ? null : c.WindowTitleContains.Trim();
        c.KickCategoryId = string.IsNullOrWhiteSpace(c.KickCategoryId) ? null : c.KickCategoryId.Trim();
        c.Notes = string.IsNullOrWhiteSpace(c.Notes) ? null : c.Notes.Trim();
        return c;
    }

    /// <summary>שם קובץ בלי נתיב ועם .exe, שומר על האותיות המקוריות.</summary>
    private static string DisplayName(string exe)
    {
        var s = exe.Trim();
        var slash = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
        if (slash >= 0) s = s[(slash + 1)..];
        if (!s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s += ".exe";
        return s;
    }

    private static bool SameRule(GameMapping a, GameMapping b) =>
        a.NormalizedExecutable == b.NormalizedExecutable &&
        string.Equals((a.WindowTitleContains ?? "").Trim(), (b.WindowTitleContains ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private void TrySave(MappingDatabase db)
    {
        try { JsonStore.Save(FilePath, db); }
        catch (Exception ex) { _log?.Log(LogLevel.Warning, "שמירת קובץ הכללים נכשלה", ex.Message); }
    }
}
