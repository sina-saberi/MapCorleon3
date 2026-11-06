using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MapCorleon.Models;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Dialogs;

public partial class TimelineDialog : Window
{
    private readonly ShellViewModel shell;
    private readonly ITimelineService timelines;
    private bool busy;

    public TimelineDialog(ShellViewModel shell)
    {
        InitializeComponent();
        this.shell = shell;
        timelines = App.Services.GetRequiredService<ITimelineService>();
        busy = true;
        DataContext = shell;
        RefreshCards();
        busy = false;
    }

    private void RefreshCards()
    {
        var canDelete = shell.Timelines.Count > 1;
        TimelineCards.ItemsSource = shell.Timelines.Select(timeline =>
            new TimelineCard(timeline, timeline.Id == shell.SelectedTimeline?.Id, canDelete)).ToArray();
        EmptyMessage.Visibility = shell.Timelines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddButton.IsEnabled = shell.CurrentMap is not null;
    }

    private async void Map_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (busy || MapBox.SelectedItem is not Map map || map.Id == shell.CurrentMap?.Id) return;
        await RunAsync(() => shell.LoadAsync(preferredMapId: map.Id));
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TimelineCard card } && !card.IsActive)
            await RunAsync(() => shell.ChangeTimelineAsync(card.Timeline.Id));
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (shell.CurrentMap is not { } map) return;
        var editor = new TimelineNameDialog { Owner = this };
        if (editor.ShowDialog() != true) return;

        var added = new Timeline { MapId = map.Id, Name = editor.TimelineName };
        await SaveAsync(map.Id, [.. shell.Timelines, added]);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TimelineCard card }) return;
        var timeline = card.Timeline;
        var editor = new TimelineNameDialog(timeline.Name) { Owner = this };
        if (editor.ShowDialog() != true || editor.TimelineName == timeline.Name) return;

        var edited = new Timeline { Id = timeline.Id, MapId = timeline.MapId, Name = editor.TimelineName };
        await SaveAsync(timeline.MapId, shell.Timelines.Select(item => item.Id == timeline.Id ? edited : item).ToArray());
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (shell.Timelines.Count <= 1 || sender is not FrameworkElement { DataContext: TimelineCard card }) return;
        var timeline = card.Timeline;
        var confirm = MessageBox.Show(this, $"Delete “{timeline.Name}” and its progress, pins, and custom locations?",
            "Delete timeline", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        await SaveAsync(timeline.MapId, shell.Timelines.Where(item => item.Id != timeline.Id).ToArray());
    }

    private Task SaveAsync(int mapId, IReadOnlyList<Timeline> values) => RunAsync(async () =>
    {
        await timelines.SaveAsync(mapId, values);
        await shell.ReloadTimelinesAsync();
    });

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        DialogContent.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not update timelines", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            MapBox.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateTarget();
            RefreshCards();
            DialogContent.IsEnabled = true;
            busy = false;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (busy) e.Cancel = true;
    }

    private sealed record TimelineCard(Timeline Timeline, bool IsActive, bool CanDelete);
}
