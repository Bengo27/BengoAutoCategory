using System.Windows;

namespace AutoCategory.App.Views;

public partial class InputDialog : Window
{
    public string Value => ValueBox.Text.Trim();

    public InputDialog(string title, string message, string initialValue)
    {
        InitializeComponent();
        Title = title;
        HeaderText.Text = title;
        MessageText.Text = message;
        ValueBox.Text = initialValue;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0) return;
        DialogResult = true;
    }
}
