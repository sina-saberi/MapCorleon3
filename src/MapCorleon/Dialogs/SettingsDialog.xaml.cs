using System.IO;
using System.Windows;
using MapCorleon.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace MapCorleon.Dialogs;

public partial class SettingsDialog : Window
{
    private readonly ISettingsService settings;
    private readonly string originalDirectory;

    public SettingsDialog()
    {
        InitializeComponent();
        settings = App.Services.GetRequiredService<ISettingsService>();
        originalDirectory = settings.Current.DataDirectory;
        DataDirectory.Text = originalDirectory;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose MapCorleon data directory",
            InitialDirectory = Directory.Exists(DataDirectory.Text) ? DataDirectory.Text : originalDirectory
        };
        if (dialog.ShowDialog(this) == true) DataDirectory.Text = dialog.FolderName;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DataDirectory.Text))
        {
            MessageBox.Show(this, "Choose a data directory.", "Settings");
            return;
        }
        try
        {
            var fullPath = Path.GetFullPath(DataDirectory.Text.Trim());
            await settings.SaveAsync(settings.Current with { DataDirectory = fullPath });
            if (!string.Equals(fullPath, originalDirectory, StringComparison.OrdinalIgnoreCase))
                MessageBox.Show(this, "The new data directory will be used after restarting MapCorleon. Existing files were not moved.", "Restart required");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
