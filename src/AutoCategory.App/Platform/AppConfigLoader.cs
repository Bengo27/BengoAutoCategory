using System.IO;
using AutoCategory.Core.Models;
using AutoCategory.Core.Storage;

namespace AutoCategory.App.Platform;

/// <summary>
/// טוען את app_config.json שמוטמע ב-exe. קובץ באותו שם בתיקיית הנתונים (%AppData%) גובר עליו – לפיתוח ובדיקות.
/// </summary>
public static class AppConfigLoader
{
    public static AppConfig Load(string dataDirectory)
    {
        var config = LoadEmbedded() ?? new AppConfig();
        try
        {
            var overridePath = Path.Combine(dataDirectory, "app_config.json");
            var local = JsonStore.Load<AppConfig>(overridePath);
            if (local is not null)
            {
                if (!string.IsNullOrWhiteSpace(local.KickClientId)) config.KickClientId = local.KickClientId;
                if (!string.IsNullOrWhiteSpace(local.KickBrokerUrl)) config.KickBrokerUrl = local.KickBrokerUrl;
                if (!string.IsNullOrWhiteSpace(local.GitHubRepo)) config.GitHubRepo = local.GitHubRepo;
            }
        }
        catch
        {
            // קובץ מקומי פגום – מתעלמים
        }
        config.KickClientId = Clean(config.KickClientId);
        config.KickBrokerUrl = Clean(config.KickBrokerUrl)?.TrimEnd('/');
        config.GitHubRepo = Clean(config.GitHubRepo);
        return config;
    }

    private static AppConfig? LoadEmbedded()
    {
        try
        {
            using var stream = typeof(AppConfigLoader).Assembly.GetManifestResourceStream("app_config.json");
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(reader.ReadToEnd(), JsonStore.Options);
        }
        catch
        {
            return null;
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
