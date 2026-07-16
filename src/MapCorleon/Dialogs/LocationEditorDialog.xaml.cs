using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using MapCorleon.Models;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace MapCorleon.Dialogs;

public partial class LocationEditorDialog : Window
{
    readonly Location? editing;
    readonly ObservableCollection<string> images = [];
    public LocationEditorDialog(Location? location = null) { editing = location; InitializeComponent(); Loaded += LoadedDialog; }
    void LoadedDialog(object sender, RoutedEventArgs e) { if (DataContext is not ShellViewModel vm) return; CategoryBox.ItemsSource = vm.Groups.SelectMany(x => x.Categories); ImagesBox.ItemsSource = images; if (editing is null) { CategoryBox.SelectedIndex = 0; return; } Title = "Edit Custom Location"; DeleteButton.Visibility = Visibility.Visible; TitleBox.Text = editing.Title; DescriptionBox.Text = editing.DescriptionMarkdown; CategoryBox.SelectedValuePath = "Id"; CategoryBox.SelectedValue = editing.CategoryId; LatitudeBox.Text = editing.Latitude.ToString(CultureInfo.InvariantCulture); LongitudeBox.Text = editing.Longitude.ToString(CultureInfo.InvariantCulture); }
    void Images_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.gif" }; if (dialog.ShowDialog() == true) foreach (var file in dialog.FileNames) if (!images.Contains(file)) images.Add(file); }
    async void PickOnMap_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Hide();
            var point = await App.Services.GetRequiredService<IMapPickService>().PickAsync();
            LatitudeBox.Text = point.Latitude.ToString("F6", CultureInfo.InvariantCulture);
            LongitudeBox.Text = point.Longitude.ToString("F6", CultureInfo.InvariantCulture);
        }
        finally { Show(); Activate(); }
    }
    async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell || shell.SelectedTimeline is null) { MessageBox.Show(this, "Select a timeline before creating a custom location.", "Timeline required", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (string.IsNullOrWhiteSpace(TitleBox.Text) || CategoryBox.SelectedItem is not Category category || !double.TryParse(LatitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) || !double.TryParse(LongitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)) { MessageBox.Show(this, "Enter a title, category, latitude, and longitude.", "Missing information", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try
        {
            var location = editing ?? new Location { Title = "", IsCustom = true };
            location.Title = TitleBox.Text.Trim(); location.DescriptionMarkdown = DescriptionBox.Text; location.CategoryId = category.Id; location.TimelineId = shell.SelectedTimeline.Id; location.Latitude = latitude; location.Longitude = longitude; location.IsCustom = true;
            var service = App.Services.GetRequiredService<ILocationService>(); await service.SaveAsync(location); foreach (var file in images) await service.AddMediaAsync(location.Id, file);
            if (DataContext is ShellViewModel vm) { await vm.LoadAsync(vm.CurrentMapName); vm.SelectLocationById(location.Id); }
            Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save location", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (editing is null || MessageBox.Show(this, $"Delete '{editing.Title}'?", "Delete custom location", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await App.Services.GetRequiredService<ILocationService>().DeleteAsync(editing.Id);
        if (DataContext is ShellViewModel vm) await vm.LoadAsync(vm.CurrentMapName);
        Close();
    }
    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
