using System.Windows;
using System.Windows.Controls;
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
        await Task.Delay(250);
        refreshScheduled = false;
        await ReloadAsync();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

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
        if (sender is not Button { Tag: int taskId } button) return;
        button.IsEnabled = false;
        var task = (await tasks.GetTasksAsync()).FirstOrDefault(x => x.Id == taskId);
        if (task?.Status == "Running") await tasks.PauseAsync(taskId);
        else if (task is not null && task.Status != "Completed") await tasks.ResumeAsync(taskId);
        await ReloadAsync();
    }
}
