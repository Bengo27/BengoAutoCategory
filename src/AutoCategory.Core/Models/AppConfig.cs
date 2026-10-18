namespace AutoCategory.Core.Models;

/// <summary>
/// הגדרות ההפצה של התוכנה (לא של המשתמש): לאיזה שרת התחברות ולאיזה מאגר עדכונים לפנות.
/// נשמר כקובץ app_config.json שמוטמע בתוך ה-exe. אין כאן סודות – ה-Client ID ציבורי.
/// </summary>
public sealed class AppConfig
{
    /// <summary>ה-Client ID של אפליקציית הקיק המובנית.</summary>
    public string? KickClientId { get; set; }

    /// <summary>כתובת שרת ההתחברות (Cloudflare Worker), למשל https://bengo-broker.xxx.workers.dev</summary>
    public string? KickBrokerUrl { get; set; }

    /// <summary>מאגר GitHub לעדכונים, בפורמט owner/repo.</summary>
    public string? GitHubRepo { get; set; }

    public bool HasBroker => !string.IsNullOrWhiteSpace(KickClientId) && !string.IsNullOrWhiteSpace(KickBrokerUrl);
    public bool HasUpdates => !string.IsNullOrWhiteSpace(GitHubRepo) && GitHubRepo!.Contains('/');
}
