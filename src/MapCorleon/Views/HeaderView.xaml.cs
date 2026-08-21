using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MapCorleon.Dialogs;
using MapCorleon.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Views;

public partial class HeaderView : UserControl
{
    private readonly IImportTaskService tasks;
    private bool subscribed;
    public HeaderView()
    {
        InitializeComponent();
        tasks = App.Services.GetRequiredService<IImportTaskService>();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        Subscribe();
        RefreshTask();
    }
    private void Subscribe()
    {
        if (subscribed) return;
        tasks.PropertyChanged += TasksChanged;
        subscribed = true;
        RefreshTask();
    }
    private void Unsubscribe()
    {
        if (!subscribed) return;
        tasks.PropertyChanged -= TasksChanged;
        subscribed = false;
    }
    private Window Owner => Window.GetWindow(this);
    private void TasksChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.Invoke(RefreshTask);
    private void RefreshTask()
    {
        ImportButton.IsEnabled = true;
        TaskPanel.Visibility = tasks.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        TaskText.Text = tasks.ActiveTaskLabel;
        TaskProgress.Value = tasks.Percentage;
        TaskPercent.Text = $"{tasks.Percentage}%";
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsDialog { Owner = Owner }.ShowDialog();
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        new ImportDialog { Owner = Owner }.ShowDialog();
    }
    private void Downloads_Click(object sender, RoutedEventArgs e) => new DownloadTasksDialog { Owner = Owner }.ShowDialog();
    private void Timeline_Click(object sender, RoutedEventArgs e) => new TimelineDialog { Owner = Owner, DataContext = DataContext }.ShowDialog();
    private void Custom_Click(object sender, RoutedEventArgs e) => new LocationEditorDialog { Owner = Owner, DataContext = DataContext }.Show();
}
