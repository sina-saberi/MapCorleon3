using MapCorleon.Contracts;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MapCorleon.Dialogs;

public partial class ImportDialog : Window
{
    private CancellationTokenSource? cancellation;
    private readonly IImportTaskService tasks;

    public ImportDialog()
    {
        InitializeComponent();
        var resolver = App.Services.GetRequiredService<IMapProviderResolver>();
        ProviderBox.ItemsSource = resolver.All;
        ProviderBox.SelectedIndex = 0;
        tasks = App.Services.GetRequiredService<IImportTaskService>();
    }

    private void ProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        var local = ProviderBox.SelectedItem is IMapProvider { UsesLocalFile: true };
        SourceLabel.Text = local ? "KCD2 game data archive" : "Map webpage URL";
        SourceHint.Text = local
            ? "Choose Data\\IPL_GameData.pak from your KCD2 installation. Map art and locations are read from your installed files."
            : "Examples: https://fallout4map.com/far-harbor or https://mapgenie.io/witcher-3/maps/white-orchard";
        BrowseArchive.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        DownloadMedia.Visibility = DownloadTiles.Visibility = DownloadHint.Visibility =
            local ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BrowseArchive_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "KCD2 game archive (IPL_GameData.pak)|IPL_GameData.pak", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) PageUrl.Text = dialog.FileName;
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderBox.SelectedItem is not IMapProvider provider ||
            !Uri.TryCreate(PageUrl.Text.Trim(), UriKind.Absolute, out var uri) ||
            (provider.UsesLocalFile ? !uri.IsFile : uri.Scheme is not ("http" or "https")))
        {
            MessageBox.Show(this, "Select an import method and enter a valid source for it.", "Missing import input");
            return;
        }

        SetBusy(true); cancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ImportProgress>(p => { StatusText.Text = p.Stage; ProgressBar.Value = p.Percentage; });
            var request = new MapImportRequest(provider.Key, uri.ToString(),
                !provider.UsesLocalFile && DownloadMedia.IsChecked == true,
                !provider.UsesLocalFile && DownloadTiles.IsChecked == true);
            var result = await provider.ImportAsync(request, progress, cancellation.Token);
            await App.Services.GetRequiredService<ShellViewModel>().LoadAsync(result.MapName);
            if (!provider.UsesLocalFile)
                await tasks.EnqueueImportedAsync(provider, result, request.DownloadMedia, request.DownloadTiles, request.PageUrl);
            MessageBox.Show(this, $"Imported {result.MapName}: {result.Groups} groups, {result.Categories} categories, {result.Locations} locations and {result.Media} media records.", "Import complete");
            DialogResult = true;
        }
        catch (OperationCanceledException) { StatusText.Text = "Import cancelled."; }
        catch (Exception ex) { StatusText.Text = "Import failed."; MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { SetBusy(false); cancellation?.Dispose(); cancellation = null; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { if (cancellation is null) Close(); else cancellation.Cancel(); }
    private void SetBusy(bool busy)
    {
        SubmitButton.IsEnabled = !busy; ProviderBox.IsEnabled = !busy; PageUrl.IsEnabled = !busy; BrowseArchive.IsEnabled = !busy;
        DownloadMedia.IsEnabled = !busy; DownloadTiles.IsEnabled = !busy;
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
}
