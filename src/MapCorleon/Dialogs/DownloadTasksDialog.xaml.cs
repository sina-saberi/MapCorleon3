using System.Windows;
using System.Windows.Controls;
using MapCorleon.Services;
using MapCorleon.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Dialogs;

public partial class DownloadTasksDialog : Window
{
    private readonly IImportTaskService tasks;
    private bool subscribed;
    private bool reloading;
    private bool reloadPending;
    private bool refreshScheduled;
    private Map? selectedMap;

    public DownloadTasksDialog()
    {
        InitializeComponent();
        tasks = App.Services.GetRequiredService<IImportTaskService>();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!subscribed)
        {
            tasks.TasksChanged += TasksChanged;
            subscribed = true;
        }
        await ReloadAsync();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (subscribed) tasks.TasksChanged -= TasksChanged;
        subscribed = false;
    }

    private void TasksChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(ScheduleReloadAsync);
    private async Task ScheduleReloadAsync()
    {
        if (refreshScheduled) return;
        refreshScheduled = true;
        await Task.Delay(750);
        refreshScheduled = false;
        await ReloadAsync();
    }
    private async void ChooseMap_Click(object sender, RoutedEventArgs e)
    {
        ChooseMapButton.IsEnabled = false;
        try
        {
            var picker = new MapPickerDialog(await tasks.GetDownloadableMapsAsync(), downloading: true) { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedMap is not { } map) return;
            selectedMap = map;
            SelectedMapName.Text = map.DisplayName;
            AddTiles.IsEnabled = map.Config is { TilePattern.Length: > 0 } config && TileCoverage.Create(config) is not null;
            AddTiles.IsChecked = false;
            AddMedia.IsChecked = true;
            DownloadOptions.Visibility = Visibility.Visible;
            AddStatus.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowAddStatus($"Could not load maps: {ex.Message}");
        }
        finally { ChooseMapButton.IsEnabled = true; }
    }

    private async void AddTask_Click(object sender, RoutedEventArgs e)
    {
        if (selectedMap is not { } map)
        {
            ShowAddStatus("Select an imported map.");
            return;
        }
        var media = AddMedia.IsChecked == true;
        var tiles = AddTiles.IsChecked == true;
        if (!media && !tiles)
        {
            ShowAddStatus("Select media, tiles, or both.");
            return;
        }

        AddButton.IsEnabled = ChooseMapButton.IsEnabled = false;
        try
        {
            var count = await tasks.EnqueueAsync(map.Id, media, tiles);
            ShowAddStatus(count == 1
                ? "Added 1 separate download item."
                : $"Added {count} separate download items.");
            DownloadOptions.Visibility = Visibility.Collapsed;
            selectedMap = null;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowAddStatus($"Could not add download: {ex.Message}");
        }
        finally { AddButton.IsEnabled = ChooseMapButton.IsEnabled = true; }
    }
    private void ShowAddStatus(string text)
    {
        AddStatus.Text = text;
        AddStatus.Visibility = Visibility.Visible;
    }
    private async Task ReloadAsync()
    {
        if (reloading) { reloadPending = true; return; }
        reloading = true;
        try
        {
            do
            {
                reloadPending = false;
                TaskList.ItemsSource = await tasks.GetTasksAsync();
            } while (reloadPending);
        }
        finally { reloading = false; }
    }

    private async void TaskAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int taskId, DataContext: MapCorleon.Models.DownloadTask task } button) return;
        button.IsEnabled = false;
        if (task.Status == "Running") await tasks.PauseAsync(taskId);
        else if (task.Status != "Completed") await tasks.ResumeAsync(taskId);
        _ = ScheduleReloadAsync();
    }

    private async void CancelTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int taskId, DataContext: MapCorleon.Models.DownloadTask task } button) return;
        if (MessageBox.Show(this,
                $"Cancel and remove the download task for {task.MapDisplayName}?\n\nDownloaded files and map/media database records will not be deleted.",
                "Cancel download task", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        button.IsEnabled = false;
        await tasks.CancelAsync(taskId);
        _ = ScheduleReloadAsync();
    }
}
