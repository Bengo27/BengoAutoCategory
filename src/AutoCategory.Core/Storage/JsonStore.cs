using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace AutoCategory.Core.Storage;

/// <summary>קריאה/כתיבה בטוחה של קבצי JSON (כתיבה לקובץ זמני ואז החלפה, כדי לא להשחית קובץ בקריסה).</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // שומר עברית קריאה בקובץ במקום א...
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>שומר עותק של קובץ פגום כדי שהמשתמש לא יאבד אותו.</summary>
    public static void BackupCorrupt(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Copy(path, $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch { /* best effort */ }
    }
}
