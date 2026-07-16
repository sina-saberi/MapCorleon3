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
    public HeaderView()
    {
        InitializeComponent();
        tasks = App.Services.GetRequiredService<IImportTaskService>();
        tasks.PropertyChanged += TasksChanged;
        Unloaded += (_, _) => tasks.PropertyChanged -= TasksChanged;
        RefreshTask();
    }
    private Window Owner => Window.GetWindow(this);
    private void TasksChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.Invoke(RefreshTask);
    private void RefreshTask()
    {
        ImportButton.IsEnabled = !tasks.IsRunning;
        TaskPanel.Visibility = tasks.IsRunning || !string.IsNullOrWhiteSpace(tasks.Status) ? Visibility.Visible : Visibility.Collapsed;
        TaskText.Text = tasks.Status;
        TaskProgress.Value = tasks.Percentage;
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsDialog { Owner = Owner }.ShowDialog();
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!tasks.IsRunning) new ImportDialog { Owner = Owner }.ShowDialog();
    }
    private void Timeline_Click(object sender, RoutedEventArgs e) => new TimelineDialog { Owner = Owner, DataContext = DataContext }.ShowDialog();
    private void Custom_Click(object sender, RoutedEventArgs e) => new LocationEditorDialog { Owner = Owner, DataContext = DataContext }.Show();
}
