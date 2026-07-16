using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MapCorleon.Dialogs;
using MapCorleon.Models;
using MapCorleon.ViewModels;

namespace MapCorleon.Views;

public partial class RightSidebarView : UserControl
{
    private static readonly Regex Links = new(@"\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)|\[(?<bracket>https?://[^\]]+|mapcorleon://[^\]]+)\]|(?<bare>https?://\S+|mapcorleon://\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private Media? selectedMedia;

    public RightSidebarView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RefreshLocation();
        Loaded += (_, _) => RefreshLocation();
    }

    private void RefreshLocation()
    {
        if (DataContext is not ShellViewModel vm) return;
        vm.PropertyChanged -= ViewModelChanged;
        vm.PropertyChanged += ViewModelChanged;
        ShowLocation(vm.SelectedLocation);
    }

    private void ViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.SelectedLocation)) ShowLocation((DataContext as ShellViewModel)?.SelectedLocation);
    }

    private void ShowLocation(Location? location)
    {
        RenderDescription(location?.DescriptionMarkdown ?? string.Empty);
        ThumbnailList.SelectedIndex = location?.Media.Count > 0 ? 0 : -1;
        UpdateHero();
    }

    private void RenderDescription(string markdown)
    {
        DescriptionText.Inlines.Clear();
        var position = 0;
        foreach (Match match in Links.Matches(markdown))
        {
            if (match.Index > position) DescriptionText.Inlines.Add(new Run(markdown[position..match.Index]));
            var url = match.Groups["url"].Success ? match.Groups["url"].Value : match.Groups["bracket"].Success ? match.Groups["bracket"].Value : match.Groups["bare"].Value.TrimEnd('.', ',', ';');
            var label = match.Groups["label"].Success ? match.Groups["label"].Value : InternalTitle(url) ?? url;
            var link = new Hyperlink(new Run(label)) { Foreground = new SolidColorBrush(Color.FromRgb(107, 164, 255)), Tag = url, ToolTip = InternalTitle(url) is null ? url : "Open this location" };
            link.Click += Link_Click;
            DescriptionText.Inlines.Add(link);
            position = match.Index + match.Length;
        }
        if (position < markdown.Length) DescriptionText.Inlines.Add(new Run(markdown[position..]));
    }

    private static string? InternalTitle(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "mapcorleon" ? "View referenced location" : null;
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        var url = (sender as Hyperlink)?.Tag as string;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "mapcorleon" && int.TryParse(uri.AbsolutePath.Trim('/'), out var id))
            (DataContext as ShellViewModel)?.SelectLocationById(id);
        else if (!string.IsNullOrWhiteSpace(url)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void Thumbnail_SelectionChanged(object sender, SelectionChangedEventArgs e) { selectedMedia = ThumbnailList.SelectedItem as Media; UpdateHero(); }
    private void UpdateHero()
    {
        HeroImage.Source = selectedMedia is null ? null : new System.Windows.Media.Imaging.BitmapImage(new Uri(selectedMedia.RelativePath, UriKind.RelativeOrAbsolute));
        HeroCaption.Text = selectedMedia?.Caption ?? "Image";
        var total = ThumbnailList.Items.Count; MediaCountText.Text = total == 0 ? "" : $"{ThumbnailList.SelectedIndex + 1} / {total}";
    }
    private void Hero_Click(object sender, RoutedEventArgs e)
    {
        if ((DataContext as ShellViewModel)?.SelectedLocation is not { } location || selectedMedia is null) return;
        new MediaViewerDialog(location.Media.ToList(), Math.Max(0, ThumbnailList.SelectedIndex)) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
    private void Edit_Click(object sender, RoutedEventArgs e) { var vm = DataContext as ShellViewModel; new LocationEditorDialog(vm?.SelectedLocation) { Owner = Window.GetWindow(this), DataContext = DataContext }.Show(); }
    private void Move_Click(object sender, RoutedEventArgs e) => (DataContext as ShellViewModel)?.FocusSelectedLocation();
}
