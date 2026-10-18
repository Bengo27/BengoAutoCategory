using AutoCategory.Core.Models;

namespace AutoCategory.Core.Services;

public interface IGameDetectionService
{
    DetectionResult Detect(ForegroundInfo foreground, IReadOnlyCollection<string> runningExecutables);
}

/// <summary>
/// משווה את התוכנה שבחזית (וגם תהליכים שרצים ברקע) מול מסד הכללים.
/// </summary>
public sealed class GameDetectionService : IGameDetectionService
{
    public const int ExactConfidence = 100;
    public const int AmbiguousConfidence = 80;

    private readonly ICategoryMappingService _mappings;

    public GameDetectionService(ICategoryMappingService mappings) => _mappings = mappings;

    public DetectionResult Detect(ForegroundInfo foreground, IReadOnlyCollection<string> runningExecutables)
    {
        var background = DetectBackground(runningExecutables);

        if (foreground.IsEmpty)
            return new DetectionResult { Foreground = foreground, Kind = DetectionKind.None, BackgroundCategories = background };

        var exe = foreground.ExecutableName;

        // כלל מיפוי קודם לרשימת ההתעלמות (אם המשתמש מיפה בכוונה – הוא רוצה זיהוי)
        var mapping = _mappings.FindMapping(exe, foreground.WindowTitle);
        if (mapping is not null)
        {
            var ambiguous = mapping.IsAmbiguous;
            return new DetectionResult
            {
                Foreground = foreground,
                Kind = DetectionKind.Game,
                Category = mapping.Category,
                Mapping = mapping,
                Confidence = ambiguous ? AmbiguousConfidence : ExactConfidence,
                BackgroundCategories = background,
            };
        }

        if (_mappings.IsIgnored(exe))
            return new DetectionResult { Foreground = foreground, Kind = DetectionKind.Ignored, BackgroundCategories = background };

        // למשל javaw.exe בלי "Minecraft" בכותרת – תוכנה מוכרת אבל לא המשחק
        return new DetectionResult { Foreground = foreground, Kind = DetectionKind.Unknown, Confidence = 0, BackgroundCategories = background };
    }

    private IReadOnlyList<string> DetectBackground(IReadOnlyCollection<string> running)
    {
        if (running.Count == 0) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var exe in running)
        {
            var m = _mappings.FindMapping(exe, null);
            // רק כללים חד-משמעיים (בלי תנאי כותרת) נחשבים כמשחק שרץ ברקע
            if (m is null || m.IsAmbiguous || !string.IsNullOrWhiteSpace(m.WindowTitleContains)) continue;
            if (!result.Contains(m.Category, StringComparer.OrdinalIgnoreCase)) result.Add(m.Category);
        }
        return result;
    }
}
