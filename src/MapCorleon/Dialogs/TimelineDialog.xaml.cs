using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MapCorleon.Models;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Dialogs;

public partial class TimelineDialog : Window
{
    private readonly ObservableCollection<Timeline> draft = [];
    private bool ready;
    public TimelineDialog() { InitializeComponent(); TimelineList.ItemsSource = draft; }

    private void Window_Loaded(object sender, RoutedEventArgs e) { LoadDraft(); ready = true; }
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
        await vm.LoadAsync(map.Name); LoadDraft();
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
        for (var i = 0; i < draft.Count; i++) draft[i].SortOrder = i;
        var selectedName = (TimelineList.SelectedItem as Timeline)?.Name;
        await App.Services.GetRequiredService<ITimelineService>().SaveAsync(map.Id, draft);
        await vm.LoadAsync(map.Name);
        vm.SelectedTimeline = vm.Timelines.FirstOrDefault(x => x.Name == selectedName) ?? vm.Timelines.FirstOrDefault();
        DialogResult = true;
    }
}
