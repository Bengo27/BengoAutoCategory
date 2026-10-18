using System.Windows;
using System.Windows.Controls;
using AutoCategory.App.ViewModels;

namespace AutoCategory.App.Views;

public partial class KickView : UserControl
{
    public KickView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is KickViewModel old) old.CredentialsSaved -= ClearSecret;
            if (e.NewValue is KickViewModel vm) vm.CredentialsSaved += ClearSecret;
        };
    }

    private void ClearSecret() => SecretBox.Clear();

    // PasswordBox לא תומך ב-Binding (מטעמי אבטחה) – מעבירים את הערך ידנית
    private void SecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is KickViewModel vm) vm.ClientSecret = SecretBox.Password;
    }
}
