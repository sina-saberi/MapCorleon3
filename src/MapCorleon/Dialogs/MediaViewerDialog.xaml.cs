using System.IO;
using System.Windows;
using System.Windows.Input;
using MapCorleon.Models;
using Microsoft.Win32;

namespace MapCorleon.Dialogs;

public partial class MediaViewerDialog : Window
{
    private readonly IReadOnlyList<Media> media;
    public MediaViewerDialog(IReadOnlyList<Media> media, int index) { this.media = media; InitializeComponent(); Filmstrip.ItemsSource = media; Filmstrip.SelectedIndex = Math.Clamp(index, 0, Math.Max(0, media.Count - 1)); }
    private Media? Current => Filmstrip.SelectedItem as Media;
    private void ShowCurrent() { if (Current is not { } item) return; MainImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(item.RelativePath, UriKind.RelativeOrAbsolute)); CaptionText.Text = item.Caption ?? "Image"; PositionText.Text = $"{Filmstrip.SelectedIndex + 1} of {media.Count}"; }
    private void Filmstrip_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ShowCurrent();
    private void Previous() { if (media.Count > 0) Filmstrip.SelectedIndex = (Filmstrip.SelectedIndex - 1 + media.Count) % media.Count; }
    private void Next() { if (media.Count > 0) Filmstrip.SelectedIndex = (Filmstrip.SelectedIndex + 1) % media.Count; }
    private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Left) Previous(); else if (e.Key == Key.Right) Next(); }
    private void Save_Click(object sender, RoutedEventArgs e) { if (Current is not { } item || !File.Exists(item.RelativePath)) return; var dialog = new SaveFileDialog { FileName = Path.GetFileName(item.RelativePath), Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp;*.gif|All files|*.*" }; if (dialog.ShowDialog(this) == true) File.Copy(item.RelativePath, dialog.FileName, true); }
}
