using System.ComponentModel;
using System.Windows;
using MapCorleon.ViewModels;

namespace MapCorleon.Dialogs;

public partial class ProgressDialog : Window
{
    private readonly ProgressCategoriesViewModel viewModel;
    private readonly ShellViewModel shell;
    private bool isSaving;

    public ProgressDialog(ShellViewModel shell)
    {
        InitializeComponent();
        this.shell = shell;
        viewModel = new ProgressCategoriesViewModel(shell.Groups, shell.ExcludedProgressCategoryIds);
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

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (isSaving) e.Cancel = true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        isSaving = true;
        DialogContent.IsEnabled = false;
        try
        {
            await shell.SaveProgressCategoriesAsync(viewModel.GetSelections());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save progress categories", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            isSaving = false;
            DialogContent.IsEnabled = true;
        }

        DialogResult = true;
    }
}
