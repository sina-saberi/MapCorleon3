using MapCorleon.Import;
using System.IO;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Windows;
namespace MapCorleon.Dialogs;
public partial class ImportDialog : Window
{
    private CancellationTokenSource? cancellation;
    public ImportDialog() => InitializeComponent();
    void BrowseMap_Click(object s, RoutedEventArgs e) => PickJson(MapPath);
    void BrowseSprite_Click(object s, RoutedEventArgs e) => PickJson(SpritePath);
    static void PickJson(System.Windows.Controls.TextBox target) { var d = new OpenFileDialog { Filter = "JSON files|*.json|All files|*.*" }; if (d.ShowDialog() == true) target.Text = d.FileName; }
    async void Submit_Click(object s, RoutedEventArgs e)
    {
        var spriteIsRemote = Uri.TryCreate(SpritePath.Text, UriKind.Absolute, out var spriteUri) && spriteUri.Scheme is "http" or "https";
        if (string.IsNullOrWhiteSpace(GameName.Text) || !File.Exists(MapPath.Text) || (!spriteIsRemote && !File.Exists(SpritePath.Text))) { MessageBox.Show(this, "Enter a game name, select the map JSON, and provide a local sprite JSON or URL.", "Missing import input"); return; }
        SetBusy(true); cancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ImportProgress>(p => { StatusText.Text = p.Stage; ProgressBar.Value = p.Percentage; });
            var provider = App.Services.GetRequiredService<IMapImportProvider>();
            var request = new MapImportRequest(GameName.Text, MapPath.Text, SpritePath.Text);
            var result = await Task.Run(() => provider.ImportAsync(request, progress, cancellation.Token), cancellation.Token);
            await App.Services.GetRequiredService<ShellViewModel>().LoadAsync(result.MapName);
            MessageBox.Show(this, $"Imported {result.MapName}: {result.Groups} groups, {result.Categories} categories, {result.Locations} locations and {result.Media} media records.", "Import complete");
            DialogResult = true;
        }
        catch (OperationCanceledException) { StatusText.Text = "Import cancelled."; }
        catch (Exception ex) { StatusText.Text = "Import failed."; MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { SetBusy(false); cancellation?.Dispose(); cancellation = null; }
    }
    void Cancel_Click(object s, RoutedEventArgs e) { if (cancellation is null) Close(); else cancellation.Cancel(); }
    void SetBusy(bool busy) { SubmitButton.IsEnabled = !busy; GameName.IsEnabled = !busy; MapPath.IsEnabled = !busy; SpritePath.IsEnabled = !busy; ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; }
}
