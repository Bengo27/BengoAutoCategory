using System.Windows.Controls;
using System.Windows.Input;
using AutoCategory.App.ViewModels;

namespace AutoCategory.App.Views;

public partial class CategoryRulesView : UserControl
{
    public CategoryRulesView()
    {
        InitializeComponent();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CategoryRulesViewModel vm && vm.Selected is not null) vm.Edit();
    }
}
