using AutoCategory.Core.Models;

namespace AutoCategory.Core.Services;

/// <summary>
/// בוחר בכל שינוי אם לעבוד מול הפלטפורמה האמיתית (קיק) או בסימולציה,
/// לפי מצב החיבור וההגדרה "שינוי אמיתי בקיק".
/// </summary>
public sealed class SwitchingCategoryChangeService : ICategoryChangeService
{
    private readonly Func<bool> _useLive;
    private readonly ICategoryChangeService _live;
    private readonly ICategoryChangeService _simulated;

    public SwitchingCategoryChangeService(Func<bool> useLive, ICategoryChangeService live, ICategoryChangeService simulated)
    {
        _useLive = useLive;
        _live = live;
        _simulated = simulated;
    }

    public bool IsLive => _useLive();

    public string DisplayName => IsLive ? _live.DisplayName : _simulated.DisplayName;

    public Task<CategoryChangeResult> ChangeCategoryAsync(string fromCategory, string toCategory, GameMapping? mapping,
        CancellationToken cancellationToken = default) =>
        IsLive
            ? _live.ChangeCategoryAsync(fromCategory, toCategory, mapping, cancellationToken)
            : _simulated.ChangeCategoryAsync(fromCategory, toCategory, mapping, cancellationToken);
}
