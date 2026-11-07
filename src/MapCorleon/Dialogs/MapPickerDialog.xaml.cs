using System.Windows;
using System.Windows.Input;
using MapCorleon.Models;

namespace MapCorleon.Dialogs;

public partial class MapPickerDialog : Window
{
    private readonly Card[] games;
    private readonly int? activeMapId;
    private readonly bool downloading;

    public MapPickerDialog(IEnumerable<Map> maps, int? activeMapId = null, bool downloading = false)
    {
        InitializeComponent();
        this.activeMapId = activeMapId;
        this.downloading = downloading;
        if (downloading) Title = "Choose a map to download";
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

    private void ShowGames()
    {
        Heading.Text = "Choose a game";
        BackButton.Visibility = Visibility.Collapsed;
        Cards.ItemsSource = games;
        Empty.Visibility = games.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardScroll.ScrollToTop();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowGames();

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
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
