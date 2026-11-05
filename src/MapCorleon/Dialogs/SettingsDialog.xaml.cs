using System.ComponentModel;
using System.IO;
using System.Windows;
using MapCorleon.Data;
using MapCorleon.Games.Skyrim;
using MapCorleon.Models;
using MapCorleon.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace MapCorleon.Dialogs;

public partial class SettingsDialog : Window
{
    private readonly ISettingsService settings;
    private readonly string originalDirectory;
    private readonly GameOverlaySettings originalOverlay;
    private readonly HashSet<string> excludedCategories;
    private IReadOnlyList<CategoryChoice> categories = [];
    private bool isSaving;

    public SettingsDialog(IEnumerable<Map>? maps = null)
    {
        InitializeComponent();
        settings = App.Services.GetRequiredService<ISettingsService>();
        originalDirectory = settings.Current.DataDirectory;
        DataDirectory.Text = originalDirectory;
        originalOverlay = settings.Current.GameOverlays.GetValueOrDefault(SkyrimGameIdentity.OverlaySettingsKey)
            ?? GameOverlaySettings.Default;
        excludedCategories = (originalOverlay.ExcludedCategories ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        NearbyRadius.Text = originalOverlay.NearbyRadius.ToString();
        NotificationSeconds.Text = originalOverlay.NearbyNotificationSeconds.ToString();
        CustomKeys.IsChecked = originalOverlay.PanelScanCode.HasValue && originalOverlay.FollowScanCode.HasValue;

        var keys = Enumerable.Range(1, 10).Select(number => new KeyChoice($"F{number}", 0x3A + number))
            .Concat([new KeyChoice("F11", 0x57), new KeyChoice("F12", 0x58)]).ToArray();
        PanelKey.ItemsSource = keys;
        FollowKey.ItemsSource = keys;
        PanelKey.SelectedValue = originalOverlay.PanelScanCode ?? 0x41;
        FollowKey.SelectedValue = originalOverlay.FollowScanCode ?? 0x42;

        var mapIds = maps?.Where(map => SkyrimGameIdentity.IsWorldMap(GameMapDescriptorFactory.Create(map)))
            .Select(map => map.Id).ToArray() ?? [];
        Loaded += async (_, _) => await LoadCategoriesAsync(mapIds);
    }

    private async Task LoadCategoriesAsync(int[] mapIds)
    {
        CategoryStatus.Text = "Loading categories…";
        try
        {
            var factory = App.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var names = await db.Categories.AsNoTracking()
                .Where(category => mapIds.Contains(category.Group!.MapId))
                .Select(category => category.Name)
                .Distinct()
                .ToListAsync();
            categories = names.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .Select(name => new CategoryChoice(name, !excludedCategories.Contains(name))).ToList();
            CategoryList.ItemsSource = categories;
            CategoryStatus.Text = categories.Count == 0 ? "Import the Skyrim map to choose its categories." : string.Empty;
        }
        catch (Exception ex)
        {
            CategoryStatus.Text = $"Could not load categories. {ex.Message}";
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose MapCorleon data directory",
            InitialDirectory = Directory.Exists(DataDirectory.Text) ? DataDirectory.Text : originalDirectory
        };
        if (dialog.ShowDialog(this) == true) DataDirectory.Text = dialog.FolderName;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DataDirectory.Text))
        {
            MessageBox.Show(this, "Choose a data directory.", "Settings");
            return;
        }
        if (!int.TryParse(NearbyRadius.Text, out var radius) || radius is < GameOverlaySettings.MinRadius or > GameOverlaySettings.MaxRadius)
        {
            MessageBox.Show(this, $"Enter a nearby radius from {GameOverlaySettings.MinRadius} to {GameOverlaySettings.MaxRadius}.", "Settings");
            return;
        }
        if (!int.TryParse(NotificationSeconds.Text, out var seconds) ||
            seconds is < GameOverlaySettings.MinNotificationSeconds or > GameOverlaySettings.MaxNotificationSeconds)
        {
            MessageBox.Show(this, $"Enter a notification duration from {GameOverlaySettings.MinNotificationSeconds} to {GameOverlaySettings.MaxNotificationSeconds} seconds.", "Settings");
            return;
        }
        var panelScanCode = PanelKey.SelectedValue as int?;
        var followScanCode = FollowKey.SelectedValue as int?;
        if (CustomKeys.IsChecked == true && (!panelScanCode.HasValue || !followScanCode.HasValue || panelScanCode == followScanCode))
        {
            MessageBox.Show(this, "Choose two different keys for the overlay panel and Follow Player.", "Settings");
            return;
        }
        var exclusions = new HashSet<string>(excludedCategories, StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            if (category.Included) exclusions.Remove(category.Name);
            else exclusions.Add(category.Name);
        }

        isSaving = true;
        DialogContent.IsEnabled = false;
        try
        {
            var fullPath = Path.GetFullPath(DataDirectory.Text.Trim());
            var current = settings.Current;
            var gameOverlays = new Dictionary<string, GameOverlaySettings>(current.GameOverlays, StringComparer.Ordinal)
            {
                [SkyrimGameIdentity.OverlaySettingsKey] = originalOverlay with
                {
                    NearbyRadius = radius,
                    NearbyNotificationSeconds = seconds,
                    PanelScanCode = CustomKeys.IsChecked == true ? panelScanCode : null,
                    FollowScanCode = CustomKeys.IsChecked == true ? followScanCode : null,
                    ExcludedCategories = exclusions.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
                }
            };
            await settings.SaveAsync(current with { DataDirectory = fullPath, GameOverlays = gameOverlays });
            if (!string.Equals(fullPath, originalDirectory, StringComparison.OrdinalIgnoreCase))
                MessageBox.Show(this, "The new data directory will be used after restarting MapCorleon. Existing files were not moved.", "Restart required");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            isSaving = false;
            DialogContent.IsEnabled = true;
        }
        DialogResult = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (isSaving) e.Cancel = true;
    }

    private sealed record KeyChoice(string Name, int ScanCode);

    private sealed class CategoryChoice(string name, bool included)
    {
        public string Name { get; } = name;
        public bool Included { get; set; } = included;
    }
}
