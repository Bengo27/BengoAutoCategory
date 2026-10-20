using Microsoft.Win32;

namespace AutoCategory.App.Platform;

/// <summary>הפעלה עם Windows – דרך HKCU\...\Run (לא דורש הרשאות מנהל).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    /// <summary>השם ברשימת ההפעלה של Windows (שונה לכל תוכנה).</summary>
    public static string ValueName { get; set; } = "BengoAutoCategory";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                key.SetValue(ValueName, $"\"{exe}\" --minimized");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
