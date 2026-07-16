using MapCorleon.Contracts;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

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
        if (tasks.IsRunning) { SubmitButton.IsEnabled = false; StatusText.Text = "Another import or offline download is already running."; }
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (tasks.IsRunning) { MessageBox.Show(this, "Wait for the current import download to finish.", "Import busy"); return; }
        if (ProviderBox.SelectedItem is not IMapProvider provider ||
            !Uri.TryCreate(PageUrl.Text.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            MessageBox.Show(this, "Select an import method and enter a valid map webpage URL.", "Missing import input");
            return;
        }

        SetBusy(true); cancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ImportProgress>(p => { StatusText.Text = p.Stage; ProgressBar.Value = p.Percentage; });
            var request = new MapImportRequest(provider.Key, uri.ToString(), DownloadMedia.IsChecked == true, DownloadTiles.IsChecked == true);
            var result = await provider.ImportAsync(request, progress, cancellation.Token);
            await App.Services.GetRequiredService<ShellViewModel>().LoadAsync(result.MapName);
            tasks.TryStart(provider, result, request.DownloadMedia, request.DownloadTiles);
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
        SubmitButton.IsEnabled = !busy; ProviderBox.IsEnabled = !busy; PageUrl.IsEnabled = !busy;
        DownloadMedia.IsEnabled = !busy; DownloadTiles.IsEnabled = !busy;
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
}
