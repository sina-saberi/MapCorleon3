using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MapCorleon.Models;

namespace MapCorleon.Dialogs;

public partial class MediaViewerDialog : Window
{
    public MediaViewerDialog(IReadOnlyList<Media> media, int index)
    {
        InitializeComponent();
        Filmstrip.ItemsSource = media;
        if (media.Count > 0) Filmstrip.SelectedIndex = Math.Clamp(index, 0, media.Count - 1);
    }

    private void Filmstrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MainImage.Source = null;
        if (Filmstrip.SelectedItem is not Media item) return;

        Filmstrip.ScrollIntoView(item);
        if (File.Exists(item.RelativePath) ||
            Uri.TryCreate(item.RelativePath, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            try { MainImage.Source = new BitmapImage(new Uri(item.RelativePath, UriKind.RelativeOrAbsolute)); }
            catch (IOException) { }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.Key is not (Key.Left or Key.Right) || Filmstrip.Items.Count == 0) return;
        var count = Filmstrip.Items.Count;
        var direction = e.Key == Key.Left ? -1 : 1;
        Filmstrip.SelectedIndex = (Filmstrip.SelectedIndex + count + direction) % count;
        e.Handled = true;
    }
}
