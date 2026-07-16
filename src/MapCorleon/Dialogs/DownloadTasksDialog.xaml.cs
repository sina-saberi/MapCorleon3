using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using MapCorleon.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Dialogs;

public partial class DownloadTasksDialog : Window
{
    private readonly IImportTaskService tasks;
    private bool subscribed;
    private bool reloading;
    private bool reloadPending;
    private bool refreshScheduled;

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
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var path = Path.Combine(settings.Current.DataDirectory, "download-http.log");
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "No HTTP download attempts have been logged yet.", "Download HTTP log");
            return;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
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
