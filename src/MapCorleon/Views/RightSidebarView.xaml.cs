using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MapCorleon.Dialogs;
using MapCorleon.Models;
using MapCorleon.ViewModels;
using MapCorleon.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Views;

public partial class RightSidebarView : UserControl
{
    private static readonly Regex MarkdownTokens = new(
        @"\*\*(?<bold>.+?)\*\*|\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)|\[(?<bracket>https?://[^\]]+|mapcorleon://[^\]]+)\]|(?<bare>https?://\S+|mapcorleon://\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private Media? selectedMedia;
    private CancellationTokenSource? previewCancellation;

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
        _ = UpdateHeroAsync();
    }

    private void RenderDescription(string markdown)
    {
        DescriptionText.Inlines.Clear();
        var position = 0;
        foreach (Match match in MarkdownTokens.Matches(markdown))
        {
            if (match.Index > position) AddPlainText(markdown[position..match.Index]);
            if (match.Groups["bold"].Success)
            {
                DescriptionText.Inlines.Add(new Run(match.Groups["bold"].Value) { FontWeight = FontWeights.SemiBold });
                position = match.Index + match.Length;
                continue;
            }
            var url = match.Groups["url"].Success ? match.Groups["url"].Value : match.Groups["bracket"].Success ? match.Groups["bracket"].Value : match.Groups["bare"].Value.TrimEnd('.', ',', ';');
            var label = match.Groups["label"].Success ? match.Groups["label"].Value : InternalTitle(url) ?? url;
            var link = new Hyperlink(new Run(label)) { Foreground = new SolidColorBrush(Color.FromRgb(107, 164, 255)), Tag = url, ToolTip = InternalTitle(url) is null ? url : "Open this location" };
            link.Click += Link_Click;
            DescriptionText.Inlines.Add(link);
            position = match.Index + match.Length;
        }
        if (position < markdown.Length) AddPlainText(markdown[position..]);
    }

    private void AddPlainText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) DescriptionText.Inlines.Add(new LineBreak());
            if (lines[index].Length > 0) DescriptionText.Inlines.Add(new Run(lines[index]));
        }
    }

    private static string? InternalTitle(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "mapcorleon" ? "View referenced location" : null;
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        var url = (sender as Hyperlink)?.Tag as string;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "mapcorleon" && int.TryParse(uri.AbsolutePath.Trim('/'), out var id))
            (DataContext as ShellViewModel)?.SelectLocationById(id);
        else if (!string.IsNullOrWhiteSpace(url)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void Thumbnail_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedMedia = ThumbnailList.SelectedItem as Media;
        _ = UpdateHeroAsync();
    }
    private async Task UpdateHeroAsync()
    {
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        previewCancellation = new CancellationTokenSource();
        var ct = previewCancellation.Token;
        var requested = selectedMedia;
        HeroImage.Source = null;
        if (requested is not null)
        {
            try
            {
                requested.RelativePath = await App.Services.GetRequiredService<IMediaDownloadService>()
                    .EnsureDownloadedAsync(requested.Id, ct);
                if (requested == selectedMedia && System.IO.File.Exists(requested.RelativePath))
                    HeroImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(requested.RelativePath, UriKind.Absolute));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (requested == selectedMedia)
                    HeroCaption.Text = $"Image unavailable: {ex.Message}";
            }
        }
        if (requested == selectedMedia && HeroImage.Source is not null)
            HeroCaption.Text = selectedMedia?.Caption ?? "Image";
        var total = ThumbnailList.Items.Count; MediaCountText.Text = total == 0 ? "" : $"{ThumbnailList.SelectedIndex + 1} / {total}";
    }
    private void Hero_Click(object sender, RoutedEventArgs e)
    {
        if ((DataContext as ShellViewModel)?.SelectedLocation is not { } location || selectedMedia is null || !System.IO.File.Exists(selectedMedia.RelativePath)) return;
        new MediaViewerDialog(location.Media.ToList(), Math.Max(0, ThumbnailList.SelectedIndex)) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
    private void Edit_Click(object sender, RoutedEventArgs e) { var vm = DataContext as ShellViewModel; new LocationEditorDialog(vm?.SelectedLocation) { Owner = Window.GetWindow(this), DataContext = DataContext }.Show(); }
    private void Move_Click(object sender, RoutedEventArgs e) => (DataContext as ShellViewModel)?.FocusSelectedLocation();
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.SelectedLocation = null;
    }
}
