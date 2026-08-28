using System.Windows;
using MapCorleon.Models;
using MapCorleon.ViewModels;

namespace MapCorleon.Dialogs;

public partial class ProgressDialog : Window
{
    private readonly ProgressCategoriesViewModel viewModel;

    public ProgressDialog(IEnumerable<Group> groups)
    {
        InitializeComponent();
        viewModel = new ProgressCategoriesViewModel(groups);
        DataContext = viewModel;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchBox.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        viewModel.Dispose();
        base.OnClosed(e);
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
