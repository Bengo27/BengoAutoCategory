using System.Windows;

// הקבצים המשותפים עם Catego (Mvvm.cs וכו') קוראים ל-AutoCategory.App.DialogService – זו הגרסה של Studio.
namespace AutoCategory.App
{
    internal static class DialogService
    {
        public static void ShowError(string message) =>
            MessageBox.Show(message, "Catego Studio", MessageBoxButton.OK, MessageBoxImage.Warning,
                MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        public static void ShowInfo(string message, string title = "Catego Studio") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information,
                MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        public static bool Confirm(string message, string title = "אישור") =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == MessageBoxResult.Yes;
    }
}

namespace Catego.Studio.ViewModels
{
    /// <summary>עמוד שרוצה לדעת כשנכנסים אליו (כדי לרענן).</summary>
    public interface IPageViewModel
    {
        void OnNavigatedTo();
    }
}
