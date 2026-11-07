using System.ComponentModel;
using System.Windows;
using MapCorleon.Contracts;
using MapCorleon.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace MapCorleon.Dialogs;

public partial class MapPickerDialog : Window
{
    private readonly Card[] games;
    private readonly int? activeMapId;
    private readonly bool downloading;
    private bool importingLocalMap;

    public MapPickerDialog(IEnumerable<Map> maps, int? activeMapId = null, bool downloading = false)
    {
        InitializeComponent();
        this.activeMapId = activeMapId;
        this.downloading = downloading;
        if (downloading) Title = "Choose a map to download";
        if (downloading) LocalMapButton.Visibility = Visibility.Collapsed;
        games = maps.GroupBy(map => map.GameId)
            .Select(group =>
            {
                var items = group.OrderBy(map => map.Name).ToArray();
                return new Card(items[0].Game?.Name ?? "Other games",
                    $"{items.Length} {(items.Length == 1 ? "map" : "maps")}",
                    items.Select(map => map.GameArtwork).FirstOrDefault(art => art is not null), items, null);
            })
            .OrderBy(card => card.Name).ToArray();
        ShowGames();
    }

    public Map? SelectedMap { get; private set; }
    public string? ImportedMapName { get; private set; }

    private void ShowGames()
    {
        Heading.Text = "Choose a game";
        BackButton.Visibility = Visibility.Collapsed;
        Cards.ItemsSource = games;
        Empty.Visibility = games.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardScroll.ScrollToTop();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowGames();

    private async void LocalMap_Click(object sender, RoutedEventArgs e)
    {
        var archive = new OpenFileDialog { Filter = "KCD2 game archive (IPL_GameData.pak)|IPL_GameData.pak" };
        if (archive.ShowDialog(this) != true) return;
        var provider = App.Services.GetRequiredService<IMapProviderResolver>().All.Single(item => item.UsesLocalFile);
        importingLocalMap = true;
        LocalMapButton.IsEnabled = Cards.IsEnabled = BackButton.IsEnabled = false;
        try
        {
            var progress = new Progress<ImportProgress>(value => LocalMapButton.Content = value.Stage);
            var result = await provider.ImportAsync(new(provider.Key, new Uri(archive.FileName).AbsoluteUri, false, false), progress);
            ImportedMapName = result.MapName;
            importingLocalMap = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not add Sedletz", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            importingLocalMap = false;
            LocalMapButton.IsEnabled = Cards.IsEnabled = BackButton.IsEnabled = true;
            LocalMapButton.Content = "Add Sedletz from installed KCD2";
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (importingLocalMap) e.Cancel = true;
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Card card }) return;
        if (card.Map is { } map)
        {
            SelectedMap = map;
            DialogResult = true;
            return;
        }

        Heading.Text = card.Name;
        BackButton.Visibility = Visibility.Visible;
        Cards.ItemsSource = card.Maps!.Select(item => new Card(item.Name,
            item.Id == activeMapId ? "Selected map" : downloading ? "Download options" : "Open map",
            item.MapArtwork, null, item)).ToArray();
        CardScroll.ScrollToTop();
    }

    private sealed record Card(string Name, string Detail, Uri? Artwork, IReadOnlyList<Map>? Maps, Map? Map)
    {
        public string Initial => Name.Length == 0 ? "?" : Name[..1].ToUpperInvariant();
    }
}
