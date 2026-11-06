using System.Windows;
using System.Windows.Controls;
using MapCorleon.Dialogs;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Views;

public partial class LeftSidebarView : UserControl
{
    public LeftSidebarView() => InitializeComponent();

    private Window Owner => Window.GetWindow(this);

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var shell = DataContext as ShellViewModel;
        new SettingsDialog(shell?.CurrentMap, shell?.GameLaunchSettingsMap) { Owner = Owner }.ShowDialog();
    }

    private void Import_Click(object sender, RoutedEventArgs e) => new ImportDialog { Owner = Owner }.ShowDialog();

    private void Downloads_Click(object sender, RoutedEventArgs e) => new DownloadTasksDialog { Owner = Owner }.ShowDialog();

    private void Timeline_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell)
            new TimelineDialog(shell) { Owner = Owner }.ShowDialog();
    }

    private async void Custom_Click(object sender, RoutedEventArgs e)
    {
        var picker = App.Services.GetRequiredService<IMapPickService>();
        if (picker.IsPicking) return;

        try
        {
            CustomButton.IsEnabled = false;
            var position = await picker.PickAsync();
            new LocationEditorDialog(position) { Owner = Owner, DataContext = DataContext }.Show();
        }
        catch (OperationCanceledException) { }
        finally
        {
            CustomButton.IsEnabled = true;
        }
    }

    private void Progress_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell) return;

        new ProgressDialog(shell)
        {
            Owner = Window.GetWindow(this)
        }.ShowDialog();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchBox.Focus();
    }
}
