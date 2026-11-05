using System.ComponentModel;
using System.IO;
using System.Windows;
using MapCorleon.Contracts;
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
    private readonly Map? launchMap;
    private GameLaunchSettings? savedLaunchSettings;
    private readonly string? overlaySettingsKey;
    private readonly HashSet<string> excludedCategories = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CategoryChoice> categories = [];
    private bool isSaving;

    public SettingsDialog(Map? activeMap = null)
    {
        InitializeComponent();
        settings = App.Services.GetRequiredService<ISettingsService>();
        originalDirectory = settings.Current.DataDirectory;
        DataDirectory.Text = originalDirectory;
        var playerLocations = App.Services.GetRequiredService<IPlayerLocationCoordinator>();
        if (activeMap?.Config is { } config && playerLocations.GetProviderMetadata(activeMap) is { } metadata)
        {
            launchMap = activeMap;
            savedLaunchSettings = new GameLaunchSettings(config.LaunchExecutablePath, config.LaunchArguments, config.LaunchWorkingDirectory);
            LaunchMapName.Text = activeMap.DisplayName;
            LaunchDescription.Text = metadata.SettingsDescription;
            LaunchExecutableBox.Text = savedLaunchSettings.ExecutablePath ?? string.Empty;
            LaunchArgumentsBox.Text = savedLaunchSettings.Arguments ?? string.Empty;
            LaunchWorkingDirectoryBox.Text = savedLaunchSettings.WorkingDirectory ?? string.Empty;
            GameLaunchTab.Visibility = Visibility.Visible;
        }

        OverlayMapName.Text = activeMap?.DisplayName ?? "No active map";
        if (activeMap is null || !SkyrimGameIdentity.IsWorldMap(GameMapDescriptorFactory.Create(activeMap)))
        {
            OverlayOptions.Visibility = Visibility.Collapsed;
            OverlayUnavailable.Text = activeMap is null
                ? "Open a supported map to change its game overlay settings."
                : "The game overlay is unavailable for this map.";
            OverlayUnavailable.Visibility = Visibility.Visible;
            return;
        }

        overlaySettingsKey = SkyrimGameIdentity.OverlaySettingsKey;
        var originalOverlay = settings.Current.GameOverlays.GetValueOrDefault(overlaySettingsKey)
            ?? GameOverlaySettings.Default;
        excludedCategories.UnionWith(originalOverlay.ExcludedCategories ?? []);
        NearbyRadius.Text = originalOverlay.NearbyRadius.ToString();
        NotificationSeconds.Text = originalOverlay.NearbyNotificationSeconds.ToString();
        CustomKeys.IsChecked = originalOverlay.PanelScanCode.HasValue && originalOverlay.FollowScanCode.HasValue;

        var keys = Enumerable.Range(1, 10).Select(number => new KeyChoice($"F{number}", 0x3A + number))
            .Concat([new KeyChoice("F11", 0x57), new KeyChoice("F12", 0x58)]).ToArray();
        PanelKey.ItemsSource = keys;
        FollowKey.ItemsSource = keys;
        PanelKey.SelectedValue = originalOverlay.PanelScanCode ?? 0x41;
        FollowKey.SelectedValue = originalOverlay.FollowScanCode ?? 0x42;

        Loaded += async (_, _) => await LoadCategoriesAsync(activeMap.Id);
    }

    private async Task LoadCategoriesAsync(int mapId)
    {
        CategoryStatus.Text = "Loading categories…";
        CategoryStatus.Visibility = Visibility.Visible;
        try
        {
            var factory = App.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var names = await db.Categories.AsNoTracking()
                .Where(category => category.Group!.MapId == mapId)
                .Select(category => category.Name)
                .Distinct()
                .ToListAsync();
            categories = names.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .Select(name => new CategoryChoice(name, !excludedCategories.Contains(name))).ToList();
            CategoryList.ItemsSource = categories;
            CategoryStatus.Text = "This map has no categories yet.";
            CategoryStatus.Visibility = categories.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the game or mod-loader executable",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = GetExistingDirectory(LaunchExecutableBox.Text) ?? string.Empty
        };
        if (dialog.ShowDialog(this) != true) return;
        LaunchExecutableBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(LaunchWorkingDirectoryBox.Text))
            LaunchWorkingDirectoryBox.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
    }

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the game working directory",
            InitialDirectory = GetExistingDirectory(LaunchWorkingDirectoryBox.Text)
                ?? GetExistingDirectory(LaunchExecutableBox.Text) ?? string.Empty
        };
        if (dialog.ShowDialog(this) == true) LaunchWorkingDirectoryBox.Text = dialog.FolderName;
    }

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());

    private static string? GetExistingDirectory(string path)
    {
        try
        {
            var fullPath = NormalizePath(path);
            if (Directory.Exists(fullPath)) return fullPath;
            var directory = Path.GetDirectoryName(fullPath);
            return Directory.Exists(directory) ? directory : null;
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DataDirectory.Text))
        {
            MessageBox.Show(this, "Choose a data directory.", "Settings");
            return;
        }
        GameOverlaySettings? overlay = null;
        if (overlaySettingsKey is not null && !TryReadOverlaySettings(out overlay)) return;

        isSaving = true;
        DialogContent.IsEnabled = false;
        try
        {
            var fullPath = Path.GetFullPath(DataDirectory.Text.Trim());
            await SaveGameLaunchAsync();
            var updated = settings.Current with { DataDirectory = fullPath };
            if (overlaySettingsKey is not null && overlay is not null)
            {
                var gameOverlays = new Dictionary<string, GameOverlaySettings>(updated.GameOverlays, StringComparer.Ordinal)
                {
                    [overlaySettingsKey] = overlay
                };
                updated = updated with { GameOverlays = gameOverlays };
            }
            await settings.SaveAsync(updated);
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

    private async Task SaveGameLaunchAsync()
    {
        if (launchMap?.Config is not { } config) return;
        var launch = new GameLaunchSettings(
            NormalizePath(LaunchExecutableBox.Text),
            string.IsNullOrWhiteSpace(LaunchArgumentsBox.Text) ? null : LaunchArgumentsBox.Text.Trim(),
            NormalizePath(LaunchWorkingDirectoryBox.Text));
        if (launch == savedLaunchSettings) return;

        await App.Services.GetRequiredService<IMapSettingsService>().SaveGameLaunchAsync(launchMap.Id, launch);
        config.LaunchExecutablePath = launch.ExecutablePath;
        config.LaunchArguments = launch.Arguments;
        config.LaunchWorkingDirectory = launch.WorkingDirectory;
        savedLaunchSettings = launch;
    }

    private bool TryReadOverlaySettings(out GameOverlaySettings? overlay)
    {
        overlay = null;
        if (!int.TryParse(NearbyRadius.Text, out var radius) || radius is < GameOverlaySettings.MinRadius or > GameOverlaySettings.MaxRadius)
        {
            MessageBox.Show(this, $"Enter a nearby radius from {GameOverlaySettings.MinRadius} to {GameOverlaySettings.MaxRadius}.", "Settings");
            return false;
        }
        if (!int.TryParse(NotificationSeconds.Text, out var seconds) ||
            seconds is < GameOverlaySettings.MinNotificationSeconds or > GameOverlaySettings.MaxNotificationSeconds)
        {
            MessageBox.Show(this, $"Enter a notification duration from {GameOverlaySettings.MinNotificationSeconds} to {GameOverlaySettings.MaxNotificationSeconds} seconds.", "Settings");
            return false;
        }
        var panelScanCode = PanelKey.SelectedValue as int?;
        var followScanCode = FollowKey.SelectedValue as int?;
        if (CustomKeys.IsChecked == true && (!panelScanCode.HasValue || !followScanCode.HasValue || panelScanCode == followScanCode))
        {
            MessageBox.Show(this, "Choose two different keys for the overlay panel and Follow Player.", "Settings");
            return false;
        }
        var exclusions = new HashSet<string>(excludedCategories, StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            if (category.Included) exclusions.Remove(category.Name);
            else exclusions.Add(category.Name);
        }

        overlay = new GameOverlaySettings
        {
            NearbyRadius = radius,
            NearbyNotificationSeconds = seconds,
            PanelScanCode = CustomKeys.IsChecked == true ? panelScanCode : null,
            FollowScanCode = CustomKeys.IsChecked == true ? followScanCode : null,
            ExcludedCategories = exclusions.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
        return true;
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
