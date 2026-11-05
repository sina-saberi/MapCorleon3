using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MapCorleon.Contracts;
using MapCorleon.Models;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace MapCorleon.Dialogs;

public partial class TimelineDialog : Window
{
    private readonly ObservableCollection<Timeline> draft = [];
    private readonly IPlayerLocationCoordinator playerLocations;
    private readonly IMapSettingsService mapSettings;
    private bool ready;
    public TimelineDialog()
    {
        InitializeComponent();
        TimelineList.ItemsSource = draft;
        playerLocations = App.Services.GetRequiredService<IPlayerLocationCoordinator>();
        mapSettings = App.Services.GetRequiredService<IMapSettingsService>();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadDraft();
        LoadMapSettings();
        ready = true;
    }
    private void LoadDraft()
    {
        if (DataContext is not ShellViewModel vm) return;
        draft.Clear();
        foreach (var timeline in vm.Timelines) draft.Add(new Timeline { Id = timeline.Id, MapId = timeline.MapId, Name = timeline.Name, SortOrder = timeline.SortOrder });
        TimelineList.SelectedItem = draft.FirstOrDefault(x => x.Id == vm.SelectedTimeline?.Id) ?? draft.FirstOrDefault();
    }

    private async void Map_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || DataContext is not ShellViewModel vm || MapBox.SelectedItem is not Map map || map.Name == vm.CurrentMapName) return;
        await vm.LoadAsync(map.Name);
        LoadDraft();
        LoadMapSettings();
    }

    private void LoadMapSettings()
    {
        if (MapBox.SelectedItem is not Map map || playerLocations.GetProviderMetadata(map) is not { } metadata)
        {
            GameSettingsPanel.Visibility = Visibility.Collapsed;
            LaunchExecutableBox.Clear();
            LaunchArgumentsBox.Clear();
            LaunchWorkingDirectoryBox.Clear();
            return;
        }

        GameSettingsPanel.Visibility = Visibility.Visible;
        GameSettingsTitle.Text = metadata.SettingsTitle;
        GameSettingsDescription.Text = metadata.SettingsDescription;
        LaunchExecutableBox.Text = map.Config?.LaunchExecutablePath ?? string.Empty;
        LaunchArgumentsBox.Text = map.Config?.LaunchArguments ?? string.Empty;
        LaunchWorkingDirectoryBox.Text = map.Config?.LaunchWorkingDirectory ?? string.Empty;
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the game or mod-loader executable",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };
        var currentDirectory = GetExistingDirectory(LaunchExecutableBox.Text);
        if (currentDirectory is not null) dialog.InitialDirectory = currentDirectory;
        if (dialog.ShowDialog(this) != true) return;
        LaunchExecutableBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(LaunchWorkingDirectoryBox.Text))
            LaunchWorkingDirectoryBox.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
    }

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the game working directory" };
        var initialDirectory = GetExistingDirectory(LaunchWorkingDirectoryBox.Text)
            ?? GetExistingDirectory(LaunchExecutableBox.Text);
        if (initialDirectory is not null) dialog.InitialDirectory = initialDirectory;
        if (dialog.ShowDialog(this) == true) LaunchWorkingDirectoryBox.Text = dialog.FolderName;
    }
    private async void Timeline_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        NameBox.Text = (TimelineList.SelectedItem as Timeline)?.Name ?? string.Empty;
        if (ready && DataContext is ShellViewModel vm && TimelineList.SelectedItem is Timeline timeline && timeline.Id > 0) await vm.ChangeTimelineAsync(timeline.Id);
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim(); if (string.IsNullOrWhiteSpace(name)) { NameBox.Focus(); return; }
        var timeline = new Timeline { MapId = (MapBox.SelectedItem as Map)?.Id ?? 0, Name = name, SortOrder = draft.Count };
        draft.Add(timeline); TimelineList.SelectedItem = timeline; NameBox.SelectAll(); NameBox.Focus();
    }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (TimelineList.SelectedItem is not Timeline timeline || string.IsNullOrWhiteSpace(NameBox.Text)) return;
        var index = TimelineList.SelectedIndex; timeline.Name = NameBox.Text.Trim(); TimelineList.Items.Refresh(); TimelineList.SelectedIndex = index;
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (TimelineList.SelectedItem is not Timeline timeline) return;
        var index = TimelineList.SelectedIndex; draft.Remove(timeline); TimelineList.SelectedIndex = Math.Min(index, draft.Count - 1);
    }
    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int delta)
    {
        var from = TimelineList.SelectedIndex; var to = from + delta; if (from < 0 || to < 0 || to >= draft.Count) return;
        draft.Move(from, to); TimelineList.SelectedIndex = to;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm || MapBox.SelectedItem is not Map map) return;
        try
        {
            for (var i = 0; i < draft.Count; i++) draft[i].SortOrder = i;
            var selectedName = (TimelineList.SelectedItem as Timeline)?.Name;
            if (playerLocations.Supports(map))
            {
                await mapSettings.SaveGameLaunchAsync(map.Id, new GameLaunchSettings(
                    NormalizePath(LaunchExecutableBox.Text),
                    LaunchArgumentsBox.Text,
                    NormalizePath(LaunchWorkingDirectoryBox.Text)));
            }
            await App.Services.GetRequiredService<ITimelineService>().SaveAsync(map.Id, draft);
            await vm.LoadAsync(map.Name, selectedName);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save map settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());

    private static string? GetExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var fullPath = Path.GetFullPath(path.Trim());
        if (Directory.Exists(fullPath)) return fullPath;
        var directory = Path.GetDirectoryName(fullPath);
        return directory is not null && Directory.Exists(directory) ? directory : null;
    }
}
