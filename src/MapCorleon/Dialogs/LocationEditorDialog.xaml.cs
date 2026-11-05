using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MapCorleon.Models;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace MapCorleon.Dialogs;

public partial class LocationEditorDialog : Window
{
    private readonly Location? editing;
    private readonly ObservableCollection<SelectedImage> images = [];
    private MapPosition position;

    public LocationEditorDialog(MapPosition position)
        : this(null, position) { }

    public LocationEditorDialog(Location location)
        : this(location, new MapPosition(location.Latitude, location.Longitude)) { }

    private LocationEditorDialog(Location? location, MapPosition position)
    {
        editing = location;
        this.position = position;
        InitializeComponent();
        Loaded += LoadedDialog;
    }

    private void LoadedDialog(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;

        CategoryBox.ItemsSource = vm.Groups.SelectMany(group => group.Categories);
        ImagesBox.ItemsSource = images;
        if (editing is null)
        {
            CategoryBox.SelectedIndex = 0;
            return;
        }

        Title = "Edit Custom Location";
        DialogHeading.Text = "Edit custom location";
        DialogSubtitle.Text = "Update this marker's details or choose a new map position.";
        DeleteButton.Visibility = Visibility.Visible;
        TitleBox.Text = editing.Title;
        DescriptionBox.Text = editing.DescriptionMarkdown;
        CategoryBox.SelectedValuePath = "Id";
        CategoryBox.SelectedValue = editing.CategoryId;
    }

    private void Images_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.gif"
        };
        if (dialog.ShowDialog() != true) return;

        foreach (var file in dialog.FileNames)
            if (!images.Any(image => image.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                images.Add(new SelectedImage(file));
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: SelectedImage image })
            images.Remove(image);
    }

    private async void PickAgain_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Hide();
            position = await App.Services.GetRequiredService<IMapPickService>().PickAsync();
        }
        catch (OperationCanceledException) { }
        finally
        {
            Show();
            Activate();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell || shell.SelectedTimeline is null)
        {
            MessageBox.Show(this, "Select a timeline before creating a custom location.", "Timeline required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(TitleBox.Text) || CategoryBox.SelectedItem is not Category category)
        {
            MessageBox.Show(this, "Enter a title and category.", "Missing information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var location = editing ?? new Location { Title = string.Empty, IsCustom = true };
            location.Title = TitleBox.Text.Trim();
            location.DescriptionMarkdown = DescriptionBox.Text;
            location.CategoryId = category.Id;
            location.TimelineId = shell.SelectedTimeline.Id;
            location.Latitude = position.Latitude;
            location.Longitude = position.Longitude;
            location.IsCustom = true;

            var service = App.Services.GetRequiredService<ILocationService>();
            await service.SaveAsync(location);
            foreach (var image in images)
                await service.AddMediaAsync(location.Id, image.FilePath);

            await shell.LoadAsync(shell.CurrentMapName);
            shell.SelectLocationById(location.Id);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save location", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (editing is null || MessageBox.Show(this, $"Delete '{editing.Title}'?", "Delete custom location",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await App.Services.GetRequiredService<ILocationService>().DeleteAsync(editing.Id);
        if (DataContext is ShellViewModel vm)
            await vm.LoadAsync(vm.CurrentMapName);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record SelectedImage(string FilePath)
    {
        public string FileName => Path.GetFileName(FilePath);
    }
}
