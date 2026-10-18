using System.Windows.Controls;
using System.Windows.Input;
using AutoCategory.App.ViewModels;

namespace AutoCategory.App.Views;

public partial class SiteRulesView : UserControl
{
    public SiteRulesView()
    {
        InitializeComponent();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SiteRulesViewModel vm && vm.Selected is not null) vm.Edit();
    }
}
